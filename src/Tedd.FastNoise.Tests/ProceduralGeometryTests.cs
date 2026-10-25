// Purpose: verify footprint geometry independently of graph compilation and vector width.
// Responsibilities: cover coarse thin surfaces, hollow interiors, boundaries, invalid boxes and backend parity.
// Design intent: test spatial coverage rather than center samples so meaningful structures survive LOD sampling.
using System;
using System.Collections.Generic;
using System.Numerics;
using Tedd.FastNoise.Procedural;

namespace Tedd.FastNoise.Tests;

public sealed class ProceduralGeometryTests
{
    [Theory]
    [InlineData(ProceduralType.Float32)]
    [InlineData(ProceduralType.Float64)]
    public void ThinSurfacesAndHollowInteriorsMatchAcrossCpuBackends(ProceduralType type)
    {
        var g = new ProceduralGraph();
        ProceduralValue C(double x) => g.Convert(g.Constant(x), type);
        var minimum = (g.Input(0, type), g.Input(1, type), g.Input(2, type));
        var maximum = (g.Input(3, type), g.Input(4, type), g.Input(5, type));
        var wall = g.IntersectsBox(minimum, maximum, (C(-7), C(0), C(-12)), (C(-6), C(18), C(12)));
        var shell = g.IntersectsBoxShell(minimum, maximum, (C(-8), C(0), C(-12)), (C(8), C(20), C(12)),
            (C(-7), C(1), C(-11)), (C(7), C(19), C(11)));
        var program = g.Compile(wall, shell);
        List<double> inputs = [];
        List<double> expected = [];
        foreach (int step in new[] { 1, 2, 4, 8, 16, 32 })
        for (int x = -32; x <= 32; x += step)
        for (int y = -16; y <= 32; y += step)
        for (int z = -32; z <= 32; z += step)
        {
            inputs.AddRange([x, y, z, x + step, y + step, z + step]);
            expected.Add(Overlap(x, y, z, x + step, y + step, z + step, -7, 0, -12, -6, 18, 12) ? 1 : 0);
            // A box shell consists of six non-overlapping solid slabs. This reference avoids
            // reproducing the graph's clipped-intersection containment implementation.
            bool occupied = Overlap(x, y, z, x + step, y + step, z + step, -8, 0, -12, -7, 20, 12)
                || Overlap(x, y, z, x + step, y + step, z + step, 7, 0, -12, 8, 20, 12)
                || Overlap(x, y, z, x + step, y + step, z + step, -7, 0, -12, 7, 1, 12)
                || Overlap(x, y, z, x + step, y + step, z + step, -7, 19, -12, 7, 20, 12)
                || Overlap(x, y, z, x + step, y + step, z + step, -7, 1, -12, 7, 19, -11)
                || Overlap(x, y, z, x + step, y + step, z + step, -7, 1, 11, 7, 19, 12);
            expected.Add(occupied ? 1 : 0);
        }
        var input = inputs.ToArray();
        var actual = new double[expected.Count];
        foreach (var backend in new[] { NoiseBackend.Scalar, NoiseBackend.Simd, NoiseBackend.Parallel })
        {
            program.Fill(input, actual, input.Length / 6, backend);
            Assert.Equal(expected, actual);
        }
        // Include a vector tail and interpreter fallback without reinterpreting the whole exhaustive grid.
        int tailCount = Vector<float>.Count * 2 + 3;
        for (int i = 0; i < tailCount; i++)
        {
            int index = input.Length / 6 - 1 - i;
            program.EvaluateInterpreted(input.AsSpan(index * 6, 6), actual.AsSpan(0, 2));
            Assert.Equal(expected[index * 2], actual[0]);
            Assert.Equal(expected[index * 2 + 1], actual[1]);
        }
    }

    [Fact]
    public void HalfOpenBoundsRejectTouchingEmptyReversedAndNanIntervals()
    {
        var g = new ProceduralGraph();
        var minimum = (g.Input(0), g.Input(1), g.Input(2));
        var maximum = (g.Input(3), g.Input(4), g.Input(5));
        var program = g.Compile(g.IntersectsBox(minimum, maximum,
            (g.Constant(0d), g.Constant(0d), g.Constant(0d)),
            (g.Constant(1d), g.Constant(1d), g.Constant(1d))));
        double[] output = new double[1];
        foreach (double[] input in new double[][]
        {
            [1, 0, 0, 2, 1, 1], [-1, 0, 0, 0, 1, 1], [0, 0, 0, 1, 0, 1],
            [1, 0, 0, 0, 1, 1], [0, 0, double.NaN, 1, 1, 1]
        })
        {
            program.Evaluate(input, output);
            Assert.Equal(0, output[0]);
        }
        program.Evaluate([0, 0, 0, 1, 1, 1], output);
        Assert.Equal(1, output[0]);
    }

    [Theory]
    [InlineData(-1, 2, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(0.25, 0.75, 1)]
    public void InnerSubtractionCanTouchExtendPastOrBeEmpty(double innerMin, double innerMax, double expected)
    {
        var g = new ProceduralGraph();
        (ProceduralValue, ProceduralValue, ProceduralValue) V(double x) => (g.Constant(x), g.Constant(x), g.Constant(x));
        var graph = g.Compile(g.IntersectsBoxShell(V(-2), V(3), V(0), V(1), V(innerMin), V(innerMax)));
        double[] output = new double[1];
        graph.Evaluate([], output);
        Assert.Equal(expected, output[0]);
        Assert.Equal(0, graph.InputCount);
    }

    [Fact]
    public void BoundsRequireOneGraphAndOneFloatingPrecision()
    {
        var g = new ProceduralGraph(); var other = new ProceduralGraph();
        var zero = (g.Constant(0d), g.Constant(0d), g.Constant(0d));
        var one = (g.Constant(1d), g.Constant(1d), g.Constant(1d));
        Assert.Throws<ArgumentException>(() => g.IntersectsBox(zero, one, zero, (other.Constant(1d), one.Item2, one.Item3)));
        Assert.Throws<ArgumentException>(() => g.IntersectsBox(zero, one, zero, (g.Constant(1f), one.Item2, one.Item3)));
        var integer = (g.Constant(0), g.Constant(0), g.Constant(0));
        Assert.Throws<ArgumentException>(() => g.IntersectsBox(integer, integer, integer, integer));
    }

    private static bool Overlap(double x0, double y0, double z0, double x1, double y1, double z1,
        double a0, double b0, double c0, double a1, double b1, double c1) =>
        Math.Min(x1, a1) - Math.Max(x0, a0) > 0
        && Math.Min(y1, b1) - Math.Max(y0, b0) > 0
        && Math.Min(z1, c1) - Math.Max(z0, c0) > 0;
}
