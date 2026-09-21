// Generates configuration-specialized Vulkan kernels from optimized procedural DAGs.
// Shared noise primitives and precise intermediates preserve operation order while graph
// folding and SPIR-V optimization remove static work, unused branches and repeated expressions.
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Tedd.FastNoise.Procedural;

namespace Tedd.FastNoise.Gpu;

/// <summary>Compiles immutable procedural graphs into inspectable GLSL and Vulkan SPIR-V.</summary>
/// <remarks>Inputs and outputs are sample-major interleaved doubles. Typed intermediate arithmetic
/// retains its declared precision. Double-precision Vulkan support must be enabled by the caller.
/// Noise coordinates must remain within the underlying int32 lattice range; arbitrary graph
/// transforms cannot be range-validated statically. Float64 Pow is rejected because Vulkan GLSL
/// has no matching built-in; approximate float32 substitution would change terrain semantics.</remarks>
public static class VulkanGraphShaderCompiler
{
    // A graph may feed several cube-face terrain pipelines. Keep device-independent compilation
    // attached to its immutable identity, without retaining abandoned worlds or theme snapshots.
    private static readonly ConditionalWeakTable<CompiledProceduralGraph, Lazy<byte[]>> CompiledShaders = new();
    /// <summary>Generates one specialized shader. No graph interpreter executes on the device.</summary>
    public static string GenerateSource(CompiledProceduralGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var source = new StringBuilder(VulkanNoiseProducer.CreateNoiseSource().Replace(
            "#version 450\n", "#version 450\n#extension GL_ARB_gpu_shader_fp64 : require\n", StringComparison.Ordinal));
        source.AppendLine("layout(local_size_x=64) in;");
        source.AppendLine("layout(set=0,binding=0,std430) readonly buffer InputValues { double inputs[]; };");
        source.AppendLine("layout(set=0,binding=1,std430) writeonly buffer OutputValues { double outputs[]; };");
        source.AppendLine("layout(push_constant) uniform GraphParameters { dvec4 originStep; uvec4 extentMode; } gp;");
        source.AppendLine("uint graphHash(uint x) { x^=x>>16u; x*=0x7feb352du; x^=x>>15u; x*=0x846ca68bu; return x^(x>>16u); }");
        source.AppendLine("float graphMin(float a,float b) { return isnan(a) ? a : isnan(b) ? b : a==b ? (a==0.0 ? uintBitsToFloat(floatBitsToUint(a)|floatBitsToUint(b)) : a) : min(a,b); }");
        source.AppendLine("float graphMax(float a,float b) { return isnan(a) ? a : isnan(b) ? b : a==b ? (a==0.0 ? uintBitsToFloat(floatBitsToUint(a)&floatBitsToUint(b)) : a) : max(a,b); }");
        source.AppendLine("double graphMin(double a,double b) { return isnan(a) ? a : isnan(b) ? b : a==b ? (a==0.0LF ? packDouble2x32(unpackDouble2x32(a)|unpackDouble2x32(b)) : a) : min(a,b); }");
        source.AppendLine("double graphMax(double a,double b) { return isnan(a) ? a : isnan(b) ? b : a==b ? (a==0.0LF ? packDouble2x32(unpackDouble2x32(a)&unpackDouble2x32(b)) : a) : max(a,b); }");
        source.AppendLine("int graphInt(double a) { return isnan(a) ? 0 : a<=-2147483648.0LF ? int(0x80000000u) : a>=2147483647.0LF ? 2147483647 : int(a); }");
        source.AppendLine("uint graphUInt(double a) { return isnan(a)||a<=0.0LF ? 0u : a>=4294967295.0LF ? 4294967295u : uint(a); }");
        foreach (var node in graph.Nodes)
        {
            if (node.Operation is ProceduralOperation.Noise2D or ProceduralOperation.Noise3D) AppendNoise(source, node);
            if (node.Operation == ProceduralOperation.Lookup)
            {
                source.Append("const ").Append(TypeName(node.Type)).Append(" table").Append(node.Id).Append("[]= ")
                    .Append(TypeName(node.Type)).Append("[](").AppendJoin(',', node.Table!.Select(v => Literal(v, node.Type))).AppendLine(");");
            }
        }
        source.AppendLine("void main() { uint sampleIndex=gl_GlobalInvocationID.x; uint count=gp.extentMode.x*gp.extentMode.y*gp.extentMode.z; if(sampleIndex>=count) return;");
        source.AppendLine("uvec3 coordinate=uvec3(sampleIndex%gp.extentMode.x,(sampleIndex/gp.extentMode.x)%gp.extentMode.y,sampleIndex/(gp.extentMode.x*gp.extentMode.y));");
        foreach (var node in graph.Nodes)
        {
            source.Append(node.Type is ProceduralType.Float32 or ProceduralType.Float64 ? "precise " : "")
                .Append(TypeName(node.Type)).Append(" n").Append(node.Id).Append('=').Append(Expression(graph, node)).AppendLine(";");
        }
        for (int i = 0; i < graph.Outputs.Count; i++)
            source.Append("outputs[sampleIndex*").Append(graph.Outputs.Count).Append("u+").Append(i).Append("u]=double(n").Append(graph.Outputs[i]).AppendLine(");");
        return source.AppendLine("}").ToString();
    }

    /// <summary>Compiles once per configured graph; cache the resulting pipeline across dispatches.</summary>
    public static byte[] Compile(CompiledProceduralGraph graph) => (byte[])CompileCached(graph).Clone();

    internal static byte[] CompileCached(CompiledProceduralGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return CompiledShaders.GetValue(graph, static key => new Lazy<byte[]>(
            () => VulkanNoiseProducer.CompileSource(GenerateSource(key), optimize: true))).Value;
    }

    internal static string GenerateTerrainSource(CompiledProceduralGraph voxelGraph, int columnOutputs, int verticalAxis,
        int upSign, double seaLevelRadius, uint[] paletteFaces)
    {
        string full = GenerateSource(voxelGraph);
        var source = new StringBuilder(full[..full.IndexOf("void main()", StringComparison.Ordinal)]
            .Replace("layout(local_size_x=64) in;", "layout(local_size_x=4,local_size_y=4,local_size_z=4) in;", StringComparison.Ordinal)
            .Replace("writeonly buffer OutputValues { double outputs[]; }", "buffer OutputValues { uint words[]; }", StringComparison.Ordinal)
            .Replace("uvec4 extentMode; } gp;", "uvec4 extentMode; uvec4 options; } gp;", StringComparison.Ordinal));
        source.AppendLine("layout(set=0,binding=2,std430) readonly buffer SkylightValues { uint skyWords[]; };");
        source.AppendLine("uint skyAt(uint i) { return (skyWords[i>>2u]>>((i&3u)*8u))&255u; }");
        source.Append("const uint palette[]=uint[](").AppendJoin(',', paletteFaces.Select(v => v.ToString(CultureInfo.InvariantCulture) + "u")).AppendLine(");");
        source.AppendLine("shared uint cells[64]; shared uint lights[64]; shared uint brickOffset; shared uint brickCount;");
        source.AppendLine("void main() { uint local=(gl_LocalInvocationID.x*4u+gl_LocalInvocationID.y)*4u+gl_LocalInvocationID.z;");
        source.Append("if(gp.extentMode.w==3u) { for(uint i=gl_LocalInvocationIndex;i<").Append(paletteFaces.Length)
            .AppendLine("u;i+=64u) words[words[0]+i]=palette[i]; return; }");
        source.AppendLine("uvec3 coordinate=gl_GlobalInvocationID; uint side=gp.extentMode.x;");
        int u = verticalAxis == 0 ? 1 : 0, v = verticalAxis == 2 ? 1 : 2;
        source.Append("uint column=coordinate[").Append(u).Append("]+side*coordinate[").Append(v).AppendLine("];");
        source.Append("precise double elevation=").Append(Literal(upSign, ProceduralType.Float64)).Append("*(gp.originStep[")
            .Append(verticalAxis).Append("]+double(coordinate[").Append(verticalAxis).Append("])*gp.originStep.w)-")
            .Append(Literal(seaLevelRadius, ProceduralType.Float64)).AppendLine(";");
        foreach (var node in voxelGraph.Nodes)
        {
            string expression = node.Operation == ProceduralOperation.Input
                ? ConvertValue(node.InputIndex == columnOutputs ? "elevation" : $"inputs[column*{columnOutputs}u+{node.InputIndex}u]", ProceduralType.Float64, node.Type)
                : Expression(voxelGraph, node);
            source.Append(node.Type is ProceduralType.Float32 or ProceduralType.Float64 ? "precise " : "")
                .Append(TypeName(node.Type)).Append(" n").Append(node.Id).Append('=').Append(expression).AppendLine(";");
        }
        source.Append("uint material=uint(n").Append(voxelGraph.Outputs[0]).AppendLine(");");
        source.AppendLine("uint skyIndex=(coordinate.x*side+coordinate.y)*side+coordinate.z;");
        source.AppendLine("uint sky=gp.options.x; if(material!=0u) { if(gp.options.y==1u) sky=skyAt(skyIndex); else if(gp.options.y==2u) { uint pitch=side+2u; uint center=((coordinate.x+1u)*pitch+coordinate.y+1u)*pitch+coordinate.z+1u; sky=max(skyAt(center),max(skyAt(center-1u),skyAt(center+1u))); sky=max(sky,max(skyAt(center-pitch),skyAt(center+pitch))); sky=max(sky,max(skyAt(center-pitch*pitch),skyAt(center+pitch*pitch))); } }");
        source.AppendLine("cells[local]=material==0u ? 0u : 0xff000000u|material; lights[local]=material==0u ? 0u : 0xff000000u|(sky<<16u); barrier();");
        source.AppendLine("if(local==0u) { bool same=true,empty=true; for(uint i=0u;i<64u;i++) { same=same&&cells[i]==cells[0]&&lights[i]==lights[0]; empty=empty&&cells[i]==0u; } brickCount=empty ? 0u : (same ? 1u : 64u); brickOffset=empty ? 0u : atomicAdd(words[0],brickCount*2u); uint bricks=side/4u; words[4u+(gl_WorkGroupID.x*bricks+gl_WorkGroupID.y)*bricks+gl_WorkGroupID.z]=brickOffset|(brickCount==1u ? 0x80000000u : 0u); } barrier();");
        source.AppendLine("if(local<brickCount) { words[brickOffset+local*2u]=cells[local]; words[brickOffset+local*2u+1u]=lights[local]; } }");
        return source.ToString();
    }

    private static string Expression(CompiledProceduralGraph graph, ProceduralNode n)
    {
        string a = "n" + n.A, b = "n" + n.B, c = "n" + n.C;
        string Binary(string op) => $"({a}{op}{b})";
        string Wrapped(string op) => n.Type == ProceduralType.Int32 ? $"int(uint({a}){op}uint({b}))" : Binary(op);
        return n.Operation switch
        {
            ProceduralOperation.Constant => Literal(n.Constant, n.Type),
            ProceduralOperation.Input => ConvertValue((n.InputIndex < 3
                ? $"gp.extentMode.w==1u ? gp.originStep[{n.InputIndex}]+double(coordinate[{n.InputIndex}])*gp.originStep.w : inputs[sampleIndex*{graph.InputCount}u+{n.InputIndex}u]"
                : $"inputs[sampleIndex*{graph.InputCount}u+{n.InputIndex}u]"), ProceduralType.Float64, n.Type),
            ProceduralOperation.Add => Wrapped("+"), ProceduralOperation.Subtract => Wrapped("-"),
            ProceduralOperation.Multiply => Wrapped("*"), ProceduralOperation.Divide => Binary("/"),
            ProceduralOperation.Negate => n.Type == ProceduralType.Int32 ? $"int(0u-uint({a}))" : $"(-{a})",
            ProceduralOperation.Min => $"{(n.Type is ProceduralType.Float32 or ProceduralType.Float64 ? "graphMin" : "min")}({a},{b})",
            ProceduralOperation.Max => $"{(n.Type is ProceduralType.Float32 or ProceduralType.Float64 ? "graphMax" : "max")}({a},{b})",
            ProceduralOperation.Abs => $"abs({a})", ProceduralOperation.Floor => $"floor({a})", ProceduralOperation.Sqrt => $"sqrt({a})",
            ProceduralOperation.Pow when n.Type == ProceduralType.Float32 => $"pow({a},{b})",
            ProceduralOperation.LessThan => Binary("<"), ProceduralOperation.LessThanOrEqual => Binary("<="),
            ProceduralOperation.GreaterThan => Binary(">"), ProceduralOperation.GreaterThanOrEqual => Binary(">="),
            ProceduralOperation.Equal => Binary("=="), ProceduralOperation.NotEqual => Binary("!="),
            ProceduralOperation.And => Binary("&&"), ProceduralOperation.Or => Binary("||"), ProceduralOperation.Not => $"(!{a})",
            ProceduralOperation.BitAnd => Binary("&"), ProceduralOperation.BitOr => Binary("|"), ProceduralOperation.BitXor => Binary("^"),
            ProceduralOperation.ShiftLeft => $"{TypeName(n.Type)}(uint({a})<<(uint({b})&31u))", ProceduralOperation.ShiftRight => $"({a}>>(uint({b})&31u))",
            ProceduralOperation.Select => $"({a}?{b}:{c})",
            ProceduralOperation.Convert => ConvertValue(a, graph.Nodes[n.A].Type, n.Type),
            ProceduralOperation.Hash => $"graphHash(uint({a}))",
            ProceduralOperation.Lookup => $"table{n.Id}[clamp(int({a}),0,{n.Table!.Count - 1})]",
            ProceduralOperation.Noise2D => $"noise{n.Id}(vec3(float({a}),float({b}),0.0),true)",
            ProceduralOperation.Noise3D => $"noise{n.Id}(vec3(float({a}),float({b}),float({c})),false)",
            _ => throw new NotSupportedException($"GPU graph operation {n.Operation} with {n.Type} is unsupported.")
        };
    }

    private static string ConvertValue(string expression, ProceduralType from, ProceduralType to) =>
        from is ProceduralType.Float32 or ProceduralType.Float64 && to is ProceduralType.Int32 or ProceduralType.UInt32
            ? $"{(to == ProceduralType.Int32 ? "graphInt" : "graphUInt")}(double({expression}))"
            : $"{TypeName(to)}({expression})";

    private static void AppendNoise(StringBuilder source, ProceduralNode node)
    {
        var r = node.NoiseRequest!.Value;
        if (!VulkanNoiseProducer.Supports(r.NoiseType, r.FractalType))
            throw new NotSupportedException($"GPU graph noise {r.NoiseType}/{r.FractalType} is unsupported.");
        string id = node.Id.ToString(CultureInfo.InvariantCulture);
        source.Append("struct NoiseParameters").Append(id).AppendLine(" { ivec4 algorithm; vec4 fractal; vec4 shaping; uvec4 material; };");
        source.Append("const NoiseParameters").Append(id).Append(" p").Append(id).Append("=NoiseParameters").Append(id)
            .Append("(ivec4(").Append(r.Seed).Append(',').Append((int)r.NoiseType).Append(',').Append((int)r.FractalType).Append(',').Append(r.Octaves)
            .Append("),vec4(").AppendJoin(',', new[] { r.Frequency, r.Lacunarity, r.Gain, r.FractalBounding }.Select(v => Literal(v, ProceduralType.Float32)))
            .Append("),vec4(").AppendJoin(',', new[] { r.WeightedStrength, r.PingPongStrength, r.LastOctaveFade, 0f }.Select(v => Literal(v, ProceduralType.Float32)))
            .Append("),uvec4(0u,0u,").Append((uint)r.RotationType3D).AppendLine("u,0u));");
        string producer = VulkanNoiseProducer.ReadShader("producer");
        string functions = producer[producer.IndexOf("float singleNoise", StringComparison.Ordinal)..producer.IndexOf("shared uint cells", StringComparison.Ordinal)];
        functions = functions.Replace("p.", "p" + id + ".", StringComparison.Ordinal);
        foreach (string name in new[] { "singleNoise", "transform", "shape", "noise" })
            functions = Regex.Replace(functions, "\\b" + name + "\\b", name + id);
        source.AppendLine(functions);
    }

    private static string TypeName(ProceduralType type) => type switch
    { ProceduralType.Float32 => "float", ProceduralType.Float64 => "double", ProceduralType.Int32 => "int", ProceduralType.UInt32 => "uint", ProceduralType.Bool => "bool", _ => throw new ArgumentOutOfRangeException(nameof(type)) };

    private static string Literal(double value, ProceduralType type)
    {
        if (type == ProceduralType.Bool) return value == 0 ? "false" : "true";
        if (type == ProceduralType.Int32) return $"int({unchecked((uint)(int)value).ToString(CultureInfo.InvariantCulture)}u)";
        if (type == ProceduralType.UInt32) return ((uint)value).ToString(CultureInfo.InvariantCulture) + "u";
        if (type == ProceduralType.Float32 && !float.IsFinite((float)value))
            return $"uintBitsToFloat({BitConverter.SingleToUInt32Bits((float)value).ToString(CultureInfo.InvariantCulture)}u)";
        if (!double.IsFinite(value))
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value);
            return $"packDouble2x32(uvec2({((uint)bits).ToString(CultureInfo.InvariantCulture)}u,{((uint)(bits >> 32)).ToString(CultureInfo.InvariantCulture)}u))";
        }
        string result = type == ProceduralType.Float32 ? ((float)value).ToString("R", CultureInfo.InvariantCulture) : value.ToString("R", CultureInfo.InvariantCulture);
        if (!result.Contains('.') && !result.Contains('E')) result += ".0";
        return result + (type == ProceduralType.Float64 ? "LF" : "");
    }
}
