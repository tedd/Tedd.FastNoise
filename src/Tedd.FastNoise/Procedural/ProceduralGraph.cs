// Builds typed procedural programs and specializes immutable configuration before execution.
// Folding removes provable constant work and shared expressions without reassociating IEEE arithmetic.
using System;
using System.Collections.Generic;
using System.Linq;
using Tedd.FastNoise.Internal;
using Tedd.FastNoise.Internal.Kernels;

namespace Tedd.FastNoise.Procedural;

/// <summary>Builds pure, multi-output mathematical programs for CPU and GPU compilation.</summary>
/// <remarks>Configure once, compile once, and retain the immutable program. Builders are not thread-safe.
/// Constants are specialization parameters: rebuild for configuration or LOD changes. Runtime input values
/// remain variable. Arithmetic preserves operand order and precision; no fast-math identities are assumed.</remarks>
public sealed class ProceduralGraph
{
    private readonly List<ProceduralNode> _nodes = [];
    private readonly Dictionary<NodeKey, int> _interned = [];

    /// <summary>Creates a numbered runtime input; indices may be reused only with the same type.</summary>
    public ProceduralValue Input(int index, ProceduralType type = ProceduralType.Float64)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        // Record widths use the same signed length domain as Span<T>; index + 1 must remain representable.
        ArgumentOutOfRangeException.ThrowIfEqual(index, int.MaxValue);
        ValidateType(type);
        if (_nodes.Any(n => n.Operation == ProceduralOperation.Input && n.InputIndex == index && n.Type != type))
            throw new ArgumentException("An input index must have one declared type.", nameof(type));
        return Intern(new() { Operation = ProceduralOperation.Input, Type = type, InputIndex = index });
    }

    /// <summary>Creates a binary64 specialization constant.</summary>
    public ProceduralValue Constant(double value) => ConstantCore(value, ProceduralType.Float64);
    /// <summary>Creates a binary32 specialization constant.</summary>
    public ProceduralValue Constant(float value) => ConstantCore(value, ProceduralType.Float32);
    /// <summary>Creates a signed specialization constant.</summary>
    public ProceduralValue Constant(int value) => ConstantCore(value, ProceduralType.Int32);
    /// <summary>Creates an unsigned specialization constant.</summary>
    public ProceduralValue Constant(uint value) => ConstantCore(value, ProceduralType.UInt32);
    /// <summary>Creates a logical specialization constant.</summary>
    public ProceduralValue Constant(bool value) => ConstantCore(value ? 1d : 0d, ProceduralType.Bool);
    private ProceduralValue ConstantCore(double value, ProceduralType type)
        => Intern(new() { Operation = ProceduralOperation.Constant, Type = type, Constant = value });

    /// <summary>Adds matching numeric values without reassociation.</summary>
    public ProceduralValue Add(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.Add, a, b);
    /// <summary>Subtracts matching numeric values.</summary>
    public ProceduralValue Subtract(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.Subtract, a, b);
    /// <summary>Multiplies matching numeric values.</summary>
    public ProceduralValue Multiply(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.Multiply, a, b);
    /// <summary>Divides matching floating-point values.</summary>
    public ProceduralValue Divide(ProceduralValue a, ProceduralValue b) { Floating(a); return Binary(ProceduralOperation.Divide, a, b); }
    /// <summary>Returns the smaller value, propagating NaN and negative zero.</summary>
    public ProceduralValue Min(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.Min, a, b);
    /// <summary>Returns the larger value, propagating NaN and positive zero.</summary>
    public ProceduralValue Max(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.Max, a, b);
    /// <summary>Returns absolute magnitude; signed integer overflow wraps.</summary>
    public ProceduralValue Abs(ProceduralValue value) => Unary(ProceduralOperation.Abs, value);
    /// <summary>Rounds down in the original floating precision.</summary>
    public ProceduralValue Floor(ProceduralValue value) { Floating(value); return Unary(ProceduralOperation.Floor, value); }
    /// <summary>Computes square root in the original floating precision.</summary>
    public ProceduralValue Sqrt(ProceduralValue value) { Floating(value); return Unary(ProceduralOperation.Sqrt, value); }
    /// <summary>Computes power in the original floating precision.</summary>
    public ProceduralValue Pow(ProceduralValue value, ProceduralValue exponent) { Floating(value); return Binary(ProceduralOperation.Pow, value, exponent); }
    /// <summary>Negates a signed numeric value.</summary>
    public ProceduralValue Negate(ProceduralValue value)
    {
        if (value.Type == ProceduralType.UInt32) throw new ArgumentException("Unsigned values cannot be negated.", nameof(value));
        return Unary(ProceduralOperation.Negate, value);
    }
    /// <summary>Compares matching values.</summary>
    public ProceduralValue LessThan(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.LessThan, a, b, true);
    /// <summary>Compares matching values.</summary>
    public ProceduralValue LessThanOrEqual(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.LessThanOrEqual, a, b, true);
    /// <summary>Compares matching values.</summary>
    public ProceduralValue GreaterThan(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.GreaterThan, a, b, true);
    /// <summary>Compares matching values.</summary>
    public ProceduralValue GreaterThanOrEqual(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.GreaterThanOrEqual, a, b, true);
    /// <summary>Compares matching values; NaN is unequal to itself.</summary>
    public ProceduralValue Equal(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.Equal, a, b, true, allowBool: true);
    /// <summary>Compares matching values; NaN is unequal to itself.</summary>
    public ProceduralValue NotEqual(ProceduralValue a, ProceduralValue b) => Binary(ProceduralOperation.NotEqual, a, b, true, allowBool: true);
    /// <summary>Computes Boolean conjunction.</summary>
    public ProceduralValue And(ProceduralValue a, ProceduralValue b) { Boolean(a); Boolean(b); return Binary(ProceduralOperation.And, a, b, allowBool: true); }
    /// <summary>Computes Boolean disjunction.</summary>
    public ProceduralValue Or(ProceduralValue a, ProceduralValue b) { Boolean(a); Boolean(b); return Binary(ProceduralOperation.Or, a, b, allowBool: true); }
    /// <summary>Computes Boolean negation.</summary>
    public ProceduralValue Not(ProceduralValue value) { Boolean(value); return Make(new() { Operation = ProceduralOperation.Not, Type = ProceduralType.Bool, A = value.Id }); }
    /// <summary>Computes bitwise conjunction.</summary>
    public ProceduralValue BitAnd(ProceduralValue a, ProceduralValue b) { Integer(a); return Binary(ProceduralOperation.BitAnd, a, b); }
    /// <summary>Computes bitwise disjunction.</summary>
    public ProceduralValue BitOr(ProceduralValue a, ProceduralValue b) { Integer(a); return Binary(ProceduralOperation.BitOr, a, b); }
    /// <summary>Computes bitwise exclusive-or.</summary>
    public ProceduralValue BitXor(ProceduralValue a, ProceduralValue b) { Integer(a); return Binary(ProceduralOperation.BitXor, a, b); }
    /// <summary>Shifts an integer left; count must be Int32 and is masked to five bits.</summary>
    public ProceduralValue ShiftLeft(ProceduralValue value, ProceduralValue count) => Shift(ProceduralOperation.ShiftLeft, value, count);
    /// <summary>Shifts an integer right; signed inputs shift arithmetically.</summary>
    public ProceduralValue ShiftRight(ProceduralValue value, ProceduralValue count) => Shift(ProceduralOperation.ShiftRight, value, count);

    /// <summary>Selects one same-typed result. Constant conditions eliminate the entire unused dependency branch.</summary>
    public ProceduralValue Select(ProceduralValue condition, ProceduralValue whenTrue, ProceduralValue whenFalse)
    {
        Boolean(condition); Own(whenTrue); Own(whenFalse);
        if (whenTrue.Type != whenFalse.Type) throw new ArgumentException("Select arms must have matching types.");
        if (whenTrue == whenFalse) return whenTrue;
        if (_nodes[condition.Id].Operation == ProceduralOperation.Constant)
            return _nodes[condition.Id].Constant != 0 ? whenTrue : whenFalse;
        return Make(new() { Operation = ProceduralOperation.Select, Type = whenTrue.Type, A = condition.Id, B = whenTrue.Id, C = whenFalse.Id });
    }

    /// <summary>Converts explicitly. Floating-to-integer conversions saturate; NaN becomes zero.</summary>
    /// <remarks>Integer-to-integer conversions wrap modulo 2^32; Boolean conversion tests nonzero.</remarks>
    public ProceduralValue Convert(ProceduralValue value, ProceduralType type)
    {
        Own(value); ValidateType(type);
        return value.Type == type ? value : Make(new() { Operation = ProceduralOperation.Convert, Type = type, A = value.Id });
    }

    /// <summary>Clamps using ordered Min(Max(value, minimum), maximum).</summary>
    public ProceduralValue Clamp(ProceduralValue value, ProceduralValue minimum, ProceduralValue maximum) => Min(Max(value, minimum), maximum);
    /// <summary>Interpolates as a + t * (b - a), preserving separate rounding.</summary>
    public ProceduralValue Lerp(ProceduralValue a, ProceduralValue b, ProceduralValue t) => Add(a, Multiply(t, Subtract(b, a)));
    /// <summary>Evaluates clamped cubic smoothstep; equal bounds retain IEEE division semantics.</summary>
    public ProceduralValue SmoothStep(ProceduralValue minimum, ProceduralValue maximum, ProceduralValue value)
    {
        Floating(value);
        var t = Clamp(Divide(Subtract(value, minimum), Subtract(maximum, minimum)), ConstantCore(0d, value.Type), ConstantCore(1d, value.Type));
        return Multiply(Multiply(t, t), Subtract(ConstantCore(3d, value.Type), Multiply(ConstantCore(2d, value.Type), t)));
    }

    /// <summary>Applies the fixed 0x7feb352d/0x846ca68b avalanche to an unsigned integer.</summary>
    public ProceduralValue Hash(ProceduralValue value)
    {
        Own(value);
        if (value.Type != ProceduralType.UInt32) throw new ArgumentException("Hash requires UInt32.", nameof(value));
        return Make(new() { Operation = ProceduralOperation.Hash, Type = ProceduralType.UInt32, A = value.Id });
    }

    /// <summary>Copies a nonempty table, rounds entries to the declared type, and clamps an Int32 index.</summary>
    public ProceduralValue Lookup(IReadOnlyList<double> table, ProceduralValue index, ProceduralType type = ProceduralType.Float64)
    {
        ArgumentNullException.ThrowIfNull(table); Own(index); ValidateType(type);
        if (table.Count == 0) throw new ArgumentException("Lookup tables cannot be empty.", nameof(table));
        if (index.Type != ProceduralType.Int32) throw new ArgumentException("Lookup requires an Int32 index.", nameof(index));
        double[] copy = new double[table.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = ProceduralRuntime.ConvertBoundary(table[i], type);
        return Make(new() { Operation = ProceduralOperation.Lookup, Type = type, A = index.Id, Table = Array.AsReadOnly(copy) });
    }

    /// <summary>Snapshots a generator and resolves its LOD at a fixed step.</summary>
    public ProceduralValue Noise2D(NoiseGenerator noise, ProceduralValue x, ProceduralValue y, float step = 1f)
    {
        ArgumentNullException.ThrowIfNull(noise);
        var request = noise.CreateRequest(new GridRegion3D(0, 0, 0, 1, 1, 1, step));
        return Noise(request, x, y, default, false);
    }
    /// <summary>Snapshots a generator and resolves its LOD at a fixed step.</summary>
    public ProceduralValue Noise3D(NoiseGenerator noise, ProceduralValue x, ProceduralValue y, ProceduralValue z, float step = 1f)
    {
        ArgumentNullException.ThrowIfNull(noise);
        return Noise(noise.CreateRequest(new GridRegion3D(0, 0, 0, 1, 1, 1, step)), x, y, z, true);
    }
    /// <summary>Imports a compiled stack with exact blend association and fixed LOD.</summary>
    public ProceduralValue Stack3D(CompiledNoiseStack stack, ProceduralValue x, ProceduralValue y, ProceduralValue z, float step = 1f)
        => Stack(stack, x, y, z, step, true);
    /// <summary>Imports a compiled stack with exact blend association and fixed LOD.</summary>
    public ProceduralValue Stack2D(CompiledNoiseStack stack, ProceduralValue x, ProceduralValue y, float step = 1f)
        => Stack(stack, x, y, default, step, false);

    /// <summary>Eliminates unreachable work, compacts the DAG and generates a typed CPU delegate.</summary>
    public CompiledProceduralGraph Compile(params ProceduralValue[] outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        if (outputs.Length == 0) throw new ArgumentException("At least one output is required.", nameof(outputs));
        bool[] reachable = new bool[_nodes.Count];
        Stack<int> pending = new();
        foreach (var output in outputs) { Own(output); pending.Push(output.Id); }
        while (pending.TryPop(out int id))
        {
            if (reachable[id]) continue;
            reachable[id] = true;
            var node = _nodes[id];
            if (node.A >= 0) pending.Push(node.A);
            if (node.B >= 0) pending.Push(node.B);
            if (node.C >= 0) pending.Push(node.C);
        }
        int[] remap = new int[_nodes.Count];
        List<ProceduralNode> live = [];
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (!reachable[i]) continue;
            var n = _nodes[i];
            remap[i] = live.Count;
            live.Add(n with { Id = live.Count, A = n.A < 0 ? -1 : remap[n.A], B = n.B < 0 ? -1 : remap[n.B], C = n.C < 0 ? -1 : remap[n.C] });
        }
        return new(live.ToArray(), outputs.Select(o => remap[o.Id]).ToArray(), _nodes.Count);
    }

    private ProceduralValue Stack(CompiledNoiseStack stack, ProceduralValue x, ProceduralValue y, ProceduralValue z, float step, bool is3D)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (!float.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        Float32(x); Float32(y); if (is3D) Float32(z);
        var layers = stack.ResolveForProcedural(step);
        var result = Constant(0f);
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            var request = new NoiseFillRequest3D
            {
                Seed = layer.Seed, Frequency = layer.Frequency, NoiseType = layer.Kernel.NoiseType,
                RotationType3D = layer.Kernel.Transform3D switch
                { TransformType3D.ImproveXYPlanes => RotationType3D.ImproveXYPlanes, TransformType3D.ImproveXZPlanes => RotationType3D.ImproveXZPlanes, _ => RotationType3D.None },
                FractalType = layer.Fractal.Type, Octaves = layer.Octaves, Lacunarity = layer.Fractal.Lacunarity,
                Gain = layer.Fractal.Gain, WeightedStrength = layer.Fractal.WeightedStrength,
                PingPongStrength = layer.Fractal.PingPongStrength, FractalBounding = layer.Fractal.Bounding,
                CellularDistance = layer.Kernel.CellularDistance, CellularReturn = layer.Kernel.CellularReturn,
                CellularJitter = layer.Kernel.CellularJitter, LastOctaveFade = layer.LastOctaveFade,
                Region = new GridRegion3D(0, 0, 0, 1, 1, 1),
            };
            var value = Add(Multiply(Noise(request, x, y, z, is3D), Constant(layer.Amplitude)), Constant(layer.Offset));
            result = i == 0 ? value : layer.Blend switch
            {
                LayerBlend.Add => Add(result, value), LayerBlend.Subtract => Subtract(result, value),
                LayerBlend.Multiply => Multiply(result, value), LayerBlend.Min => Min(result, value),
                LayerBlend.Max => Max(result, value), LayerBlend.Replace => value,
                LayerBlend.Lerp => Lerp(result, value, Constant(layer.BlendFactor)), _ => Add(result, value),
            };
        }
        return result;
    }

    private ProceduralValue Noise(NoiseFillRequest3D request, ProceduralValue x, ProceduralValue y, ProceduralValue z, bool is3D)
    {
        Float32(x); Float32(y); if (is3D) Float32(z);
        if (!NoisePipeline.HasWideKernel(request.NoiseType))
            throw new NotSupportedException($"Procedural programs do not yet support {request.NoiseType}.");
        return Make(new() { Operation = is3D ? ProceduralOperation.Noise3D : ProceduralOperation.Noise2D,
            Type = ProceduralType.Float32, A = x.Id, B = y.Id, C = is3D ? z.Id : -1,
            NoiseRequest = request with { Region = new GridRegion3D(0, 0, 0, 1, 1, 1) } });
    }

    private ProceduralValue Binary(ProceduralOperation operation, ProceduralValue a, ProceduralValue b, bool comparison = false, bool allowBool = false)
    {
        Own(a); Own(b);
        if (a.Type != b.Type || (!allowBool && a.Type == ProceduralType.Bool))
            throw new ArgumentException("Operands must have matching numeric types; use Convert explicitly.");
        return Make(new() { Operation = operation, Type = comparison ? ProceduralType.Bool : a.Type, A = a.Id, B = b.Id });
    }
    private ProceduralValue Unary(ProceduralOperation operation, ProceduralValue value)
    {
        Own(value);
        if (value.Type == ProceduralType.Bool) throw new ArgumentException("Numeric operand required.", nameof(value));
        return Make(new() { Operation = operation, Type = value.Type, A = value.Id });
    }
    private ProceduralValue Shift(ProceduralOperation operation, ProceduralValue value, ProceduralValue count)
    {
        Integer(value); Own(count);
        if (count.Type != ProceduralType.Int32) throw new ArgumentException("Shift count must be Int32.", nameof(count));
        return Make(new() { Operation = operation, Type = value.Type, A = value.Id, B = count.Id });
    }
    private void Own(ProceduralValue value)
    {
        if (!ReferenceEquals(value.Graph, this)) throw new ArgumentException("Edges must belong to this graph.", nameof(value));
    }
    private void Floating(ProceduralValue value) { Own(value); if (value.Type is not (ProceduralType.Float32 or ProceduralType.Float64)) throw new ArgumentException("Floating-point value required."); }
    private void Float32(ProceduralValue value) { Own(value); if (value.Type != ProceduralType.Float32) throw new ArgumentException("Noise coordinates require explicit Float32 conversion."); }
    private void Integer(ProceduralValue value) { Own(value); if (value.Type is not (ProceduralType.Int32 or ProceduralType.UInt32)) throw new ArgumentException("Integer value required."); }
    private void Boolean(ProceduralValue value) { Own(value); if (value.Type != ProceduralType.Bool) throw new ArgumentException("Boolean value required."); }
    private static void ValidateType(ProceduralType type) { if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type)); }

    private ProceduralValue Make(ProceduralNode node)
    {
        // Folding evaluates each operation in its declared precision. Identities such as x*0 or
        // (x*a)*b -> x*(a*b) are deliberately absent: they alter NaNs, signed zeros or rounding.
        if (node.A >= 0 && _nodes[node.A].Operation == ProceduralOperation.Constant
            && (node.B < 0 || _nodes[node.B].Operation == ProceduralOperation.Constant)
            && (node.C < 0 || _nodes[node.C].Operation == ProceduralOperation.Constant))
        {
            double value = ProceduralRuntime.Fold(node, _nodes);
            return ConstantCore(value, node.Type);
        }
        return Intern(node);
    }
    private ProceduralValue Intern(ProceduralNode node)
    {
        var key = new NodeKey(node.Operation, node.Type, node.A, node.B, node.C,
            BitConverter.DoubleToInt64Bits(node.Constant), node.InputIndex, node.Table,
            node.NoiseRequest is { } request ? NoiseKey.From(request) : null);
        if (_interned.TryGetValue(key, out int id)) return new(this, id, node.Type);
        id = _nodes.Count;
        _nodes.Add(node with { Id = id }); _interned.Add(key, id);
        return new(this, id, node.Type);
    }
    private readonly record struct NodeKey(ProceduralOperation Operation, ProceduralType Type,
        int A, int B, int C, long ConstantBits, int InputIndex, IReadOnlyList<double>? Table, NoiseKey? Noise);

    // Record equality on floating settings considers +0 and -0 equal. CSE must retain exact
    // settings bits, because zero signs and NaN payloads are part of ordered IEEE arithmetic.
    private readonly record struct NoiseKey(int Seed, NoiseType Algorithm, RotationType3D Rotation, FractalType Fractal,
        int Octaves, int Frequency, int Lacunarity, int Gain, int WeightedStrength, int PingPongStrength,
        int Bounding, CellularDistanceFunction Distance, CellularReturnType Return, int Jitter, int Fade)
    {
        public static NoiseKey From(NoiseFillRequest3D value) => new(value.Seed, value.NoiseType,
            value.RotationType3D, value.FractalType, value.Octaves, BitConverter.SingleToInt32Bits(value.Frequency),
            BitConverter.SingleToInt32Bits(value.Lacunarity), BitConverter.SingleToInt32Bits(value.Gain),
            BitConverter.SingleToInt32Bits(value.WeightedStrength), BitConverter.SingleToInt32Bits(value.PingPongStrength),
            BitConverter.SingleToInt32Bits(value.FractalBounding), value.CellularDistance, value.CellularReturn,
            BitConverter.SingleToInt32Bits(value.CellularJitter), BitConverter.SingleToInt32Bits(value.LastOctaveFade));
    }
}
