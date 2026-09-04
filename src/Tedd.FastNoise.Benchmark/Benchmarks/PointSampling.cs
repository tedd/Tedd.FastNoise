using BenchmarkDotNet.Attributes;
using Tedd.FastNoise.Internal;

namespace Tedd.FastNoise.Benchmark.Benchmarks;

/// <summary>
/// Single-point sampling, where there is nothing to vectorise.
/// </summary>
/// <remarks>
/// <para>
/// This is the control experiment for the whole architecture. The kernels are written once,
/// generically over an operation set, and instantiated separately for scalar and vector lanes. The
/// bet is that static abstract interface members on a struct type argument cost nothing after the
/// JIT resolves and inlines them.
/// </para>
/// <para>
/// If that bet is wrong it shows up right here: this library's scalar path would be measurably
/// slower than the hand-written reference running identical arithmetic. A near-tie means the
/// abstraction is free and the SIMD gains elsewhere are real gains rather than the recovery of
/// something given away here.
/// </para>
/// </remarks>
[NoiseBenchmark]
public class PointSampling
{
    private const int Iterations = 100_000;

    private NoiseGenerator _noise = null!;
    private FastNoiseLiteCore _reference = null!;

    /// <summary>Which algorithm is being sampled.</summary>
    [Params(NoiseType.OpenSimplex2, NoiseType.Perlin, NoiseType.Value)]
    public NoiseType Type { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _noise = new NoiseGenerator(1337) { NoiseType = Type, Frequency = 0.01f };

        _reference = new FastNoiseLiteCore(1337);
        _reference.SetNoiseType((FastNoiseLiteCore.NoiseType)Type);
        _reference.SetFrequency(0.01f);
    }

    /// <summary>The hand-written reference, one sample at a time.</summary>
    [Benchmark(Baseline = true, Description = "FastNoiseLite reference")]
    public float Reference()
    {
        float total = 0f;

        for (int i = 0; i < Iterations; i++)
        {
            total += _reference.GetNoise(i * 0.37f, i * 0.11f);
        }

        return total;
    }

    /// <summary>This library's scalar instantiation of the same kernel.</summary>
    [Benchmark(Description = "Tedd.FastNoise GetNoise")]
    public float Generic()
    {
        float total = 0f;

        for (int i = 0; i < Iterations; i++)
        {
            total += _noise.GetNoise(i * 0.37f, i * 0.11f);
        }

        return total;
    }
}
