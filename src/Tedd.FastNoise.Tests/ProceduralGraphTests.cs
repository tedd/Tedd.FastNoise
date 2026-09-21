// Verifies procedural specialization, typed arithmetic and stack import against independent scalar results.
// Bit comparisons deliberately expose altered IEEE association and LOD normalization at material thresholds.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Tedd.FastNoise.Procedural;

namespace Tedd.FastNoise.Tests;

public sealed class ProceduralGraphTests
{
    [Fact]
    public void ConstantsSharedExpressionsAndDeadOutputsAreRemoved()
    {
        var graph = new ProceduralGraph();
        var x = graph.Input(0);
        var factor = graph.Multiply(graph.Constant(2d), graph.Constant(3d));
        var shared = graph.Multiply(x, factor);
        Assert.Equal(shared, graph.Multiply(x, factor));
        _ = graph.Sqrt(graph.Input(9));
        var deadBranch = graph.Noise3D(new NoiseGenerator(), graph.Convert(x, ProceduralType.Float32), graph.Constant(2f), graph.Constant(3f));
        var selected = graph.Select(graph.Constant(true), shared, graph.Convert(deadBranch, ProceduralType.Float64));
        var compiled = graph.Compile(selected, shared + graph.Constant(1d));
        Assert.Equal(1, compiled.InputCount);
        Assert.DoesNotContain(compiled.Nodes, n => n.Operation == ProceduralOperation.Noise3D);
        Assert.Single(compiled.Nodes, n => n.Operation == ProceduralOperation.Multiply);
        double[] result = new double[2];
        compiled.Evaluate([7d], result);
        Assert.Equal(new[] { 42d, 43d }, result);
        Assert.True(compiled.Nodes.Count < compiled.BuilderNodeCount);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    [InlineData(1e30)]
    public void OrderedArithmeticRetainsIeeeBehavior(double value)
    {
        var graph = new ProceduralGraph();
        var x = graph.Input(0);
        var a = graph.Constant(1e-30);
        var b = graph.Constant(1e30);
        var compiled = graph.Compile((x * a) * b, x * graph.Constant(0d), x + graph.Constant(0d));
        double[] result = new double[3];
        compiled.Evaluate([value], result);
        double first = value * 1e-30;
        EqualBits(first * 1e30, result[0]);
        EqualBits(value * 0d, result[1]);
        EqualBits(value + 0d, result[2]);
    }

    [Fact]
    public void FloatPrecisionIsExplicitBeforePromotion()
    {
        var graph = new ProceduralGraph();
        var value = graph.Input(0, ProceduralType.Float32);
        var promoted = graph.Convert(value + graph.Constant(1f), ProceduralType.Float64);
        var wide = graph.Convert(value, ProceduralType.Float64) + graph.Constant(1d);
        double[] outputs = new double[2];
        graph.Compile(promoted, wide).Evaluate([16777216d], outputs);
        Assert.Equal(new[] { 16777216d, 16777217d }, outputs);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(16f)]
    [InlineData(128f)]
    [InlineData(1024f)]
    public void ImportedStackMatchesScalarAndSimdAtEveryLod(float step)
    {
        var stack = new NoiseStack { Lod = LodPolicy.Automatic with { CullLayers = true } };
        foreach (var blend in Enum.GetValues<LayerBlend>())
            stack.Add(new NoiseLayer { Source = new NoiseGenerator(123 + (int)blend)
            { NoiseType = NoiseType.OpenSimplex2, FractalType = FractalType.FBm, Octaves = 5,
                Frequency = 0.003f + (int)blend * 0.0001f, WeightedStrength = 0.4f },
                Amplitude = 0.6f, Offset = 0.2f, Blend = blend, BlendFactor = 0.31f,
                FeatureSize = blend == LayerBlend.Multiply ? 40f : 0f });
        var original = stack.Compile();
        var graph = new ProceduralGraph();
        var x = graph.Input(0, ProceduralType.Float32);
        var y = graph.Input(1, ProceduralType.Float32);
        var z = graph.Input(2, ProceduralType.Float32);
        var compiled = graph.Compile(graph.Stack3D(original, x, y, z, step));
        var region = new GridRegion3D(-137f, 87f, -23f, 19, 2, 2, step);
        float[] expected = original.Create(region, NoiseBackend.Scalar);
        float[] simd = original.Create(region, NoiseBackend.Simd);
        double[] output = new double[1];
        for (int i = 0; i < expected.Length; i++)
        {
            int px = i % region.Width, py = i / region.Width % region.Height, pz = i / (region.Width * region.Height);
            compiled.Evaluate([region.OriginX + px * step, region.OriginY + py * step, region.OriginZ + pz * step], output);
            EqualBits(expected[i], output[0]);
            Assert.Equal(BitConverter.SingleToInt32Bits(expected[i]), BitConverter.SingleToInt32Bits(simd[i]));
        }
    }

    [Fact]
    public void IntegerHashAndClampedImmutableTablesAreDeterministic()
    {
        var graph = new ProceduralGraph();
        var key = graph.Input(0, ProceduralType.UInt32);
        var index = graph.Input(1, ProceduralType.Int32);
        double[] table = [11, 23, 47];
        var program = graph.Compile(graph.Hash(key), graph.Lookup(table, index), graph.Add(key, graph.Constant(1u)));
        table[0] = 999;
        double[] actual = new double[3];
        program.Evaluate([uint.MaxValue, -5], actual);
        uint expected = uint.MaxValue;
        unchecked { expected ^= expected >> 16; expected *= 0x7feb352d; expected ^= expected >> 15; expected *= 0x846ca68b; expected ^= expected >> 16; }
        Assert.Equal((double)expected, actual[0]);
        Assert.Equal(11, actual[1]);
        Assert.Equal(0, actual[2]);
        program.Evaluate([0, 50], actual);
        Assert.Equal(47, actual[1]);
    }

    [Fact]
    public void CastsSaturateAndWrapByExplicitContract()
    {
        var graph = new ProceduralGraph();
        var value = graph.Input(0);
        var signed = graph.Convert(value, ProceduralType.Int32);
        var program = graph.Compile(signed, graph.Convert(value, ProceduralType.UInt32), graph.Convert(signed, ProceduralType.UInt32));
        double[] output = new double[3];
        program.Evaluate([double.NaN], output); Assert.Equal(new[] { 0d, 0d, 0d }, output);
        program.Evaluate([-1d], output); Assert.Equal(new[] { -1d, 0d, uint.MaxValue }, output);
        program.Evaluate([double.PositiveInfinity], output); Assert.Equal(new[] { (double)int.MaxValue, uint.MaxValue, int.MaxValue }, output);
    }

    [Fact]
    public void SnapshotIsUnaffectedByGeneratorMutationAndConcurrentExecution()
    {
        var generator = new NoiseGenerator(17) { FractalType = FractalType.Ridged, Frequency = 0.025f };
        float expected = generator.GetNoise(7.5f, -9f, 3f);
        var graph = new ProceduralGraph();
        var point = graph.Input(0, ProceduralType.Float32);
        var compiled = graph.Compile(graph.Noise3D(generator, point, graph.Constant(-9f), graph.Constant(3f)));
        generator.Seed = 999;
        Parallel.For(0, 64, _ =>
        {
            Span<double> output = stackalloc double[1];
            compiled.Evaluate([7.5], output);
            EqualBits(expected, output[0]);
        });
    }

    [Fact]
    public void MixedGraphAndTypeEdgesAreRejected()
    {
        var first = new ProceduralGraph(); var second = new ProceduralGraph();
        Assert.Throws<ArgumentException>(() => first.Add(first.Constant(1d), second.Constant(2d)));
        Assert.Throws<ArgumentException>(() => first.Add(first.Constant(1d), first.Constant(2f)));
        Assert.Throws<ArgumentException>(() => first.Compile(default(ProceduralValue)));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Input(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Input(0, (ProceduralType)100));
    }

    [Fact]
    public void NoiseSettingsCsePreservesSignedZeroBits()
    {
        var graph = new ProceduralGraph();
        var x = graph.Input(0, ProceduralType.Float32);
        var positive = new NoiseGenerator(17) { Frequency = 0f };
        var negative = new NoiseGenerator(17) { Frequency = BitConverter.Int32BitsToSingle(int.MinValue) };
        var program = graph.Compile(graph.Noise2D(positive, x, x), graph.Noise2D(negative, x, x));
        Assert.Equal(2, program.Nodes.Count(node => node.Operation == ProceduralOperation.Noise2D));
    }

    [Fact]
    public void FillValidatesLengthsAndMatchesPointEvaluation()
    {
        var graph = new ProceduralGraph(); var x = graph.Input(0);
        var program = graph.Compile(x * x, graph.Sqrt(x));
        double[] input = [0, 1, 4, 9, 16]; double[] output = new double[10];
        program.Fill(input, output, 5);
        Assert.Equal(new[] { 0d, 0d, 1d, 1d, 16d, 2d, 81d, 3d, 256d, 4d }, output);
        Assert.Throws<ArgumentException>(() => program.Fill(input, output, 6));
        Assert.Throws<ArgumentException>(() => program.Fill(output, output, 5));
    }

    [Fact]
    public void GeneratedSimdScalarAndInterpreterAgreeAcrossEveryOperation()
    {
        var graph = new ProceduralGraph();
        var d = graph.Input(0);
        var f = graph.Input(1, ProceduralType.Float32);
        var i = graph.Input(2, ProceduralType.Int32);
        var u = graph.Input(3, ProceduralType.UInt32);
        var flag = graph.Input(4, ProceduralType.Bool);
        List<ProceduralValue> results = [];
        foreach (var value in new[] { d, f, i, u })
        {
            var two = graph.Convert(graph.Constant(2d), value.Type);
            var zero = graph.Convert(graph.Constant(0d), value.Type);
            results.AddRange([value + two, value - two, value * two, graph.Min(value, zero), graph.Max(value, zero), graph.Abs(value),
                graph.LessThan(value, two), graph.LessThanOrEqual(value, two), graph.GreaterThan(value, two), graph.GreaterThanOrEqual(value, two),
                graph.Equal(value, two), graph.NotEqual(value, two), graph.Select(flag, value, two)]);
            foreach (var type in Enum.GetValues<ProceduralType>()) results.Add(graph.Convert(value, type));
            if (value.Type is ProceduralType.Float32 or ProceduralType.Float64)
                results.AddRange([value / two, graph.Floor(value), graph.Sqrt(value), graph.Pow(value, two), -value]);
            else
                results.AddRange([graph.BitAnd(value, two), graph.BitOr(value, two), graph.BitXor(value, two), graph.ShiftLeft(value, i), graph.ShiftRight(value, i)]);
        }
        results.AddRange([graph.And(flag, graph.Not(flag)), graph.Or(flag, graph.Not(flag)), graph.Equal(flag, graph.Not(flag)), graph.Hash(u)]);
        foreach (var type in Enum.GetValues<ProceduralType>())
        {
            results.Add(graph.Convert(flag, type));
            results.Add(graph.Lookup([0, 1, 3], i, type));
        }
        var program = graph.Compile(results.ToArray());
        int count = Vector<float>.Count * 3 + 3;
        double[] exceptional = [0d, BitConverter.Int64BitsToDouble(long.MinValue), 2d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 123.75, -99.5, 1e40];
        double[] inputs = new double[count * 5];
        for (int index = 0; index < count; index++)
        {
            inputs[index * 5] = exceptional[index % exceptional.Length];
            inputs[index * 5 + 1] = exceptional[(index + 2) % exceptional.Length];
            inputs[index * 5 + 2] = index % 3 == 0 ? int.MinValue : index % 3 == 1 ? -1 : int.MaxValue;
            inputs[index * 5 + 3] = index % 2 == 0 ? uint.MaxValue : 2;
            inputs[index * 5 + 4] = index % 2;
        }
        double[] expected = new double[count * program.OutputCount];
        double[] actual = new double[expected.Length];
        program.Fill(inputs, expected, count, NoiseBackend.Scalar);
        program.Fill(inputs, actual, count, NoiseBackend.Simd);
        for (int index = 0; index < expected.Length; index++) EqualBits(expected[index], actual[index]);
        for (int index = 0; index < count; index++)
            program.EvaluateInterpreted(inputs.AsSpan(index * 5, 5), actual.AsSpan(index * program.OutputCount, program.OutputCount));
        for (int index = 0; index < expected.Length; index++) EqualBits(expected[index], actual[index]);
    }

    [Theory]
    [InlineData(FractalType.None)]
    [InlineData(FractalType.FBm)]
    [InlineData(FractalType.Ridged)]
    [InlineData(FractalType.PingPong)]
    public void SimdNoiseAndDoubleShapingMatchPointExecution(FractalType fractal)
    {
        var graph = new ProceduralGraph();
        var x = graph.Input(0, ProceduralType.Float32); var y = graph.Input(1, ProceduralType.Float32); var z = graph.Input(2, ProceduralType.Float32);
        var generator = new NoiseGenerator(31337) { NoiseType = NoiseType.Perlin, FractalType = fractal, Octaves = 4,
            Frequency = 0.017f, WeightedStrength = 0.3f, Lod = LodPolicy.Automatic };
        var field = graph.Noise3D(generator, x, y, z, 16f);
        var doubleField = graph.Convert(field * graph.Constant(0.5f) + graph.Constant(0.5f), ProceduralType.Float64);
        var result = graph.Compile(field, graph.Noise2D(generator, x, z, 16f), graph.SmoothStep(graph.Constant(0.1), graph.Constant(0.7), doubleField));
        int count = Vector<float>.Count * 3 + 1;
        double[] inputs = new double[count * 3];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = (i * 41.7) - 187.0;
        double[] expected = new double[count * 3]; double[] actual = new double[expected.Length];
        result.Fill(inputs, expected, count, NoiseBackend.Scalar);
        result.Fill(inputs, actual, count, NoiseBackend.Simd);
        for (int i = 0; i < expected.Length; i++) EqualBits(expected[i], actual[i]);
    }

    [Fact]
    public void SignedZeroConstantsRemainDistinctAndConstantUnsignedShiftMasksNegativeCount()
    {
        var graph = new ProceduralGraph();
        var positive = graph.Constant(0d); var negative = graph.Constant(BitConverter.Int64BitsToDouble(long.MinValue));
        Assert.NotEqual(positive, negative);
        var result = graph.Compile(positive, negative, graph.ShiftRight(graph.Constant(uint.MaxValue), graph.Constant(-1)));
        double[] output = new double[3]; result.Evaluate([], output);
        EqualBits(0d, output[0]); EqualBits(BitConverter.Int64BitsToDouble(long.MinValue), output[1]); Assert.Equal(1d, output[2]);
    }

    private static void EqualBits(double expected, double actual)
    {
        if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual));
        else Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }
}
