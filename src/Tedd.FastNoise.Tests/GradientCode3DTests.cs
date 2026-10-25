// Verifies the transposed nibble decoder against the original table and independent gather-based execution.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.InteropServices;
using Tedd.FastNoise.Internal;
using Tedd.FastNoise.Internal.Kernels;
using Tedd.FastNoise.Procedural;

namespace Tedd.FastNoise.Tests;

public sealed class GradientCode3DTests
{
    private delegate void WideExecutor(ReadOnlySpan<double> inputs, Span<double> outputs);
    private static readonly int[] ExceptionalBits =
    [
        0, int.MinValue, 1, unchecked((int)0x80000001), 0x3F800000, unchecked((int)0xBF800000),
        0x7F800000, unchecked((int)0xFF800000), 0x7FC12345, unchecked((int)0xFFC54321),
        0x7F812345, unchecked((int)0xFF854321), 0x7F7FFFFF, unchecked((int)0xFF7FFFFF),
    ];

    [Fact]
    public void EveryIndexAndMixedLanePermutationRetainsOriginalCode()
    {
        int lanes = Vector<int>.Count;
        for (int index = 0; index < 64; index++)
        {
            Assert.Equal(Tables.GradientCodes3D[index], ScalarOps.GatherI(Tables.GradientCodes3D, index));
            var broadcast = ProceduralVectorOps.GatherI(Tables.GradientCodes3D, new Vector<int>(index));
            int[] mixed = new int[lanes];
            for (int lane = 0; lane < lanes; lane++) mixed[lane] = (index * 17 + lane * 29) & 63;
            var shuffled = ProceduralVectorOps.GatherI(Tables.GradientCodes3D, new Vector<int>(mixed));
            for (int lane = 0; lane < lanes; lane++)
            {
                Assert.Equal(Tables.GradientCodes3D[index], broadcast[lane]);
                Assert.Equal(Tables.GradientCodes3D[mixed[lane]], shuffled[lane]);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GradientArithmeticRetainsOriginalGatherBits(bool exceptional)
    {
        int lanes = Vector<float>.Count;
        for (int group = 0; group < 32; group++)
        {
            int[] seed = new int[lanes], xp = new int[lanes], yp = new int[lanes], zp = new int[lanes];
            for (int lane = 0; lane < lanes; lane++)
            {
                seed[lane] = unchecked(int.MaxValue + group * 31 + lane);
                xp[lane] = unchecked((group * lanes + lane) * Hashing.PrimeX);
                yp[lane] = unchecked((group - lane * 17) * Hashing.PrimeY);
                zp[lane] = unchecked((lane - group * 11) * Hashing.PrimeZ);
            }
            var x = Coordinates(group, exceptional); var y = Coordinates(group + 3, exceptional); var z = Coordinates(group + 7, exceptional);
            var expected = Hashing.GradCoord3<OriginalGradientOps, Vector<float>, Vector<int>>(new(seed), new(xp), new(yp), new(zp), x, y, z);
            var actual = Hashing.GradCoord3<ProceduralVectorOps, Vector<float>, Vector<int>>(new(seed), new(xp), new(yp), new(zp), x, y, z);
            EqualBits(expected, actual, $"Gradient exceptional={exceptional}, group={group}");
        }
    }

    [Fact]
    public void OtherIntegerTablesAndSlicesRetainOriginalGather()
    {
        int[] other = new int[96];
        for (int i = 0; i < other.Length; i++) other[i] = unchecked(i * 123456789 + int.MinValue);
        CompareTable(other.AsSpan(0, 64));
        CompareTable(other.AsSpan(7, 64));
        CompareTable(other.AsSpan(3, 1));
        CompareTable(Tables.GradientCodes3D.ToArray());
        CompareTable(Tables.GradientCodes3D[..31]);
        CompareTable(Tables.GradientCodes3D[1..]);
    }

    private static void CompareTable(ReadOnlySpan<int> table)
    {
        int[] indices = new int[Vector<int>.Count];
        for (int group = 0; group < 64; group++)
        {
            for (int lane = 0; lane < indices.Length; lane++) indices[lane] = (group * 17 + lane * 29) % table.Length;
            var expected = VectorOps.GatherI(table, new Vector<int>(indices));
            var actual = ProceduralVectorOps.GatherI(table, new Vector<int>(indices));
            for (int lane = 0; lane < indices.Length; lane++) Assert.Equal(expected[lane], actual[lane]);
        }
    }

    [Theory]
    [InlineData(NoiseType.OpenSimplex2)]
    [InlineData(NoiseType.Perlin)]
    public void FractalsAndRotationsRetainOriginalGatherBits(NoiseType type)
    {
        foreach (var rotation in Enum.GetValues<RotationType3D>())
        foreach (var fractal in new[] { FractalType.None, FractalType.FBm, FractalType.Ridged, FractalType.PingPong })
        foreach (int octaves in new[] { 1, 4 })
        {
            var request = new NoiseFillRequest3D
            {
                Seed = int.MaxValue - 1, Frequency = .0037f, NoiseType = type,
                RotationType3D = rotation, FractalType = fractal, Octaves = octaves,
                Gain = .65f, Lacunarity = 2.3f, WeightedStrength = .4f, PingPongStrength = 2.7f,
                FractalBounding = .49382716f, LastOctaveFade = .375f,
                Region = new GridRegion3D(0f, 0f, 0f, 1, 1, 1),
            };
            CompareNoise(request);
            CompareNoise(request with { Seed = int.MinValue, Frequency = float.MaxValue, Gain = float.MaxValue });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeGraphAndScalarTailsRetainOriginalGatherBits(bool exceptionalCoordinates)
    {
        var graph = new ProceduralGraph();
        var x = graph.Input(0, ProceduralType.Float32); var y = graph.Input(1, ProceduralType.Float32); var z = graph.Input(2, ProceduralType.Float32);
        var a = graph.Input(3, ProceduralType.Float64); var b = graph.Input(4, ProceduralType.Float64);
        List<ProceduralValue> fields = [];
        foreach (var type in new[] { NoiseType.OpenSimplex2, NoiseType.Perlin })
        foreach (var fractal in new[] { FractalType.FBm, FractalType.Ridged })
        {
            var noise = graph.Noise3D(new NoiseGenerator(int.MaxValue - fields.Count)
            {
                NoiseType = type, FractalType = fractal, Frequency = .0037f,
                Octaves = 4, WeightedStrength = .4f, RotationType3D = RotationType3D.ImproveXYPlanes,
                Lod = LodPolicy.Disabled,
            }, x, y, z);
            fields.Add(graph.Convert(graph.Min(graph.Constant(1f), graph.Max(graph.Constant(0f), (noise + graph.Constant(1f)) * graph.Constant(.5f))), ProceduralType.Float64));
        }
        fields.AddRange([a, b, a + b, a - b, a * b, a / b, graph.Min(a, b), graph.Max(a, b), fields[0] + a]);
        var program = graph.Compile(fields.ToArray());
        var original = CompileOriginalGather(program);
        int count = Vector<float>.Count * 2 + 3;
        double[] input = new double[count * program.InputCount];
        var bits = MemoryMarshal.Cast<double, long>(input.AsSpan());
        for (int i = 0; i < count; i++)
        {
            int first = i * program.InputCount;
            input[first] = i * .125; input[first + 1] = i * -.25; input[first + 2] = 11d;
            if (exceptionalCoordinates)
                for (int j = 0; j < 3; j++) input[first + j] = BitConverter.Int32BitsToSingle(ExceptionalBits[(i + j * 3) % ExceptionalBits.Length]);
            bits[first + 3] = (i & 1) == 0 ? 0x7FF0123456789ABC : unchecked((long)0xFFF056789ABC1234);
            bits[first + 4] = (i & 1) == 0 ? unchecked((long)0xFFF856789ABC1234) : 0x7FF8123456789ABC;
        }
        double[] expected = new double[count * program.OutputCount + 5], actual = new double[expected.Length];
        Array.Fill(expected, 12345d);
        int record = 0;
        if (program.IsVectorised)
            for (; record <= count - Vector<float>.Count; record += Vector<float>.Count)
                original(input.AsSpan(record * program.InputCount), expected.AsSpan(record * program.OutputCount));
        for (; record < count; record++)
            program.Evaluate(input.AsSpan(record * program.InputCount), expected.AsSpan(record * program.OutputCount));
        foreach (var backend in new[] { NoiseBackend.Auto, NoiseBackend.Simd })
        {
            Array.Fill(actual, 12345d);
            program.Fill(input, actual, count, backend);
            for (int i = 0; i < expected.Length; i++)
                Assert.True(BitConverter.DoubleToInt64Bits(expected[i]) == BitConverter.DoubleToInt64Bits(actual[i]),
                    $"Graph exceptionalXYZ={exceptionalCoordinates}, backend={backend}, element={i}; expected={BitConverter.DoubleToInt64Bits(expected[i]):X16} actual={BitConverter.DoubleToInt64Bits(actual[i]):X16}");
        }
    }

    private static void CompareNoise(NoiseFillRequest3D request)
    {
        var original = new OriginalNoise(request);
        var candidate = new ProceduralNoise(request);
        foreach (bool exceptional in new[] { false, true })
        for (int group = 0; group < 4; group++)
        {
            var x = Coordinates(group, exceptional); var y = Coordinates(group + 3, exceptional); var z = Coordinates(group + 7, exceptional);
            var expected = request.NoiseType == NoiseType.OpenSimplex2
                ? original.SampleOpenSimplex3Wide(x, y, z) : original.Sample3Wide(x, y, z);
            var actual = request.NoiseType == NoiseType.OpenSimplex2
                ? candidate.SampleOpenSimplex3Wide(x, y, z) : candidate.Sample3Wide(x, y, z);
            EqualBits(expected, actual, $"{request}; exceptional={exceptional}, group={group}");
        }
    }

    private static Vector<float> Coordinates(int offset, bool exceptional)
    {
        float[] lanes = new float[Vector<float>.Count];
        for (int lane = 0; lane < lanes.Length; lane++)
            lanes[lane] = exceptional ? BitConverter.Int32BitsToSingle(ExceptionalBits[(offset + lane) % ExceptionalBits.Length])
                : ((offset + lane * 17) % 37 - 18) * .125f;
        return new(lanes);
    }

    private static void EqualBits(Vector<float> expected, Vector<float> actual, string context)
    {
        for (int lane = 0; lane < Vector<float>.Count; lane++)
            Assert.True(BitConverter.SingleToInt32Bits(expected[lane]) == BitConverter.SingleToInt32Bits(actual[lane]),
                $"{context}; lane={lane}; expected={BitConverter.SingleToInt32Bits(expected[lane]):X8} actual={BitConverter.SingleToInt32Bits(actual[lane]):X8}");
    }

    // Same transform/fractal path as released ProceduralNoise, with the frozen gather operation set.
    private sealed class OriginalNoise
    {
        private readonly NoiseFillRequest3D _request;
        private readonly KernelConfig _kernel;
        private readonly FractalConfig _fractal;
        internal OriginalNoise(NoiseFillRequest3D request)
        {
            _request = request;
            _kernel = new NoiseGenerator(request.Seed)
            {
                NoiseType = request.NoiseType, RotationType3D = request.RotationType3D,
                CellularDistanceFunction = request.CellularDistance, CellularReturnType = request.CellularReturn,
                CellularJitter = request.CellularJitter,
            }.BuildKernelConfig();
            _fractal = new() { Type = request.FractalType, Octaves = request.Octaves, Lacunarity = request.Lacunarity,
                Gain = request.Gain, WeightedStrength = request.WeightedStrength, PingPongStrength = request.PingPongStrength,
                Bounding = request.FractalBounding };
        }
        public Vector<float> Sample3Wide(Vector<float> x, Vector<float> y, Vector<float> z)
        {
            NoisePipeline.Transform3<OriginalGradientOps, Vector<float>, Vector<int>>(_kernel.Transform3D, _request.Frequency, ref x, ref y, ref z);
            return FractalKernel.Fractal3<OriginalGradientOps, Vector<float>, Vector<int>>(_kernel, _fractal, _request.Seed, x, y, z, _request.Octaves, _request.LastOctaveFade);
        }
        public Vector<float> SampleOpenSimplex3Wide(Vector<float> x, Vector<float> y, Vector<float> z)
        {
            NoisePipeline.Transform3<OriginalGradientOps, Vector<float>, Vector<int>>(_kernel.Transform3D, _request.Frequency, ref x, ref y, ref z);
            return FractalKernel.OpenSimplex3<OriginalGradientOps, Vector<float>, Vector<int>>(_kernel, _fractal, _request.Seed, x, y, z, _request.Octaves, _request.LastOctaveFade);
        }
    }

    private static WideExecutor CompileOriginalGather(CompiledProceduralGraph program)
    {
        var input = Expression.Parameter(typeof(ReadOnlySpan<double>), "input");
        var output = Expression.Parameter(typeof(Span<double>), "output");
        var locals = program.Nodes.Select(node => Expression.Variable(ProceduralWideRuntime.ClrType(node.Type), $"original{node.Id}")).ToArray();
        List<Expression> body = [];
        foreach (var node in program.Nodes)
        {
            Expression value = node.Operation == ProceduralOperation.Input
                ? ProceduralWideRuntime.Read(input, node.InputIndex, program.InputCount, node.Type)
                : node.Operation == ProceduralOperation.Noise3D
                    ? Expression.Call(Expression.Constant(new OriginalNoise(node.NoiseRequest!.Value)),
                        typeof(OriginalNoise).GetMethod(node.NoiseRequest!.Value.NoiseType == NoiseType.OpenSimplex2
                            ? nameof(OriginalNoise.SampleOpenSimplex3Wide) : nameof(OriginalNoise.Sample3Wide))!, locals[node.A], locals[node.B], locals[node.C])
                    : ProceduralWideRuntime.Emit(node, id => locals[id], id => program.Nodes[id].Type);
            body.Add(Expression.Assign(locals[node.Id], value));
        }
        for (int i = 0; i < program.OutputCount; i++)
            body.Add(ProceduralRuntime.Call(typeof(ProceduralWideRuntime), nameof(ProceduralWideRuntime.Write),
                output, Expression.Constant(i), Expression.Constant(program.OutputCount),
                ProceduralWideRuntime.ToOutput(locals[program.Outputs[i]], program.Nodes[program.Outputs[i]].Type)));
        return Expression.Lambda<WideExecutor>(Expression.Block(locals, body), input, output).Compile();
    }
}
