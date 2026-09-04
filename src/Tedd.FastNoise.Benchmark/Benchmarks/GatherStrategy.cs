using System;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tedd.FastNoise.Internal;

namespace Tedd.FastNoise.Benchmark.Benchmarks;

/// <summary>
/// Is a hardware gather worth using for the gradient table lookups?
/// </summary>
/// <remarks>
/// <para>
/// Every gradient noise sample reads two or three floats from a lookup table at an index derived
/// from the lattice hash, and the indices differ per lane. That is the one operation in the whole
/// library with no single-instruction portable form, and it happens four to eight times per
/// sample, so it is worth knowing what it costs.
/// </para>
/// <para>
/// Two implementations exist. <c>vgatherdps</c> does it in one instruction with high latency;
/// spilling the index vector and doing scalar loads takes more instructions but keeps them short
/// and independent. Which wins is genuinely hardware-dependent -- gather has been quietly
/// deprioritised on several recent microarchitectures -- so the library picks the hardware path
/// where it exists and this benchmark is how that choice gets revisited.
/// </para>
/// <para>
/// A microbenchmark, and labelled as one: it measures the gather in isolation, not its effect on a
/// full kernel where the surrounding arithmetic hides some of the latency. Read it as an upper
/// bound on the difference, then confirm against <c>Heightmap2D</c>.
/// </para>
/// </remarks>
[NoiseBenchmark]
public class GatherStrategy
{
    private const int Vectors = 100_000;

    private Vector<int>[] _indices = [];

    [GlobalSetup]
    public void Setup()
    {
        Random random = new(Seed: 1234);
        _indices = new Vector<int>[Vectors];

        int[] lane = new int[Vector<int>.Count];

        for (int index = 0; index < Vectors; index++)
        {
            for (int i = 0; i < lane.Length; i++)
            {
                // The same masking the 2D gradient path applies: an even index into 256 floats.
                lane[i] = random.Next(0, 128) << 1;
            }

            _indices[index] = new Vector<int>(lane);
        }
    }

    /// <summary>Whatever the library chose on this machine.</summary>
    [Benchmark(Baseline = true, Description = "VectorOps.Gather (as shipped)")]
    public float Shipped()
    {
        Vector<float> total = Vector<float>.Zero;

        foreach (Vector<int> indices in _indices)
        {
            total += VectorOps.Gather(Tables.Gradients2D, indices);
        }

        return Vector.Sum(total);
    }

    /// <summary>Spill the indices and do independent scalar loads.</summary>
    [Benchmark(Description = "Software gather (spill and index)")]
    public float Software()
    {
        Vector<float> total = Vector<float>.Zero;

        foreach (Vector<int> indices in _indices)
        {
            total += VectorOps.GatherSoftware(Tables.Gradients2D, indices);
        }

        return Vector.Sum(total);
    }
}
