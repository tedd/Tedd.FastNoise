using System;
using BenchmarkDotNet.Attributes;
using Tedd.FastNoise.Internal;
using Tedd.FastNoise.V1;

namespace Tedd.FastNoise.Benchmark.Benchmarks;

/// <summary>
/// The headline question: how fast can this library produce a heightmap, and how much of that came
/// from vectorising rather than from replacing a slow 2020 implementation?
/// </summary>
/// <remarks>
/// <para>
/// Four things are measured against the same output size:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>V1Archive</b> -- the 2020 implementation, frozen in <c>archive/v1</c>. Scalar, double
/// precision, permutation-table Perlin, one point at a time. This is where the project started.
/// </description></item>
/// <item><description>
/// <b>ReferenceLoop</b> -- the FastNoiseLite reference called per sample. This is the honest
/// comparison: it is what a user would be running today, and it is already a good implementation.
/// Anything gained over this line is gained by bulk generation, not by fixing bad scalar code.
/// </description></item>
/// <item><description><b>Scalar / Simd / Parallel</b> -- this library's three CPU backends.</description></item>
/// </list>
/// <para>
/// Scalar against ReferenceLoop should be roughly a tie -- they run the same arithmetic -- and that
/// near-tie is itself a result worth having, because it says the generic ops abstraction costs
/// nothing.
/// </para>
/// </remarks>
[NoiseBenchmark]
[MemoryDiagnoser]
public class Heightmap2D
{
    private float[] _destination = [];
    private double[] _destinationV1 = [];

    private NoiseGenerator _noise = null!;
    private FastNoiseLiteCore _reference = null!;
    private OriginalGenerator _v1 = null!;

    private GridRegion2D _region;

    /// <summary>Edge length of the square heightmap.</summary>
    [Params(256, 1024)]
    public int Size { get; set; }

    /// <summary>Octaves of fractal detail. 1 isolates the kernel; 5 is a realistic terrain setting.</summary>
    [Params(1, 5)]
    public int Octaves { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _destination = new float[Size * Size];
        _destinationV1 = new double[Size * Size];
        _region = new GridRegion2D(0f, 0f, Size, Size);

        _noise = new NoiseGenerator(1337)
        {
            NoiseType = NoiseType.OpenSimplex2,
            FractalType = Octaves > 1 ? FractalType.FBm : FractalType.None,
            Octaves = Octaves,
            Frequency = 0.01f,
            ParallelThreshold = 1,
        };

        _reference = new FastNoiseLiteCore(1337);
        _reference.SetNoiseType(FastNoiseLiteCore.NoiseType.OpenSimplex2);
        _reference.SetFractalType(Octaves > 1 ? FastNoiseLiteCore.FractalType.FBm : FastNoiseLiteCore.FractalType.None);
        _reference.SetFractalOctaves(Octaves);
        _reference.SetFrequency(0.01f);

        _v1 = new OriginalGenerator(1337, 2);
    }

    /// <summary>The 2020 implementation: scalar, double precision, one sample per call.</summary>
    /// <remarks>
    /// <para>
    /// Not an apples-to-apples algorithm comparison -- it is table-based Perlin in double precision
    /// against simplex in single -- but it is where this repository actually started, and the point
    /// of keeping it is to know the size of the gap rather than to guess at it.
    /// </para>
    /// <para>
    /// The octave loop is written out here because v1 had no concept of a fractal. Without it this
    /// row would do a fraction of the work of every other row at <see cref="Octaves"/> above one and
    /// look far better than it is -- which is exactly the kind of quiet unfairness that makes a
    /// benchmark suite worse than no benchmark suite.
    /// </para>
    /// </remarks>
    [Benchmark(Description = "v1 archive (2020, scalar double)")]
    public double[] V1Archive()
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double sum = 0;
                double amplitude = 1d / 1.75d;
                double frequency = 0.01d;

                for (int octave = 0; octave < Octaves; octave++)
                {
                    sum += _v1.Perlin(x * frequency, y * frequency, 0d) * amplitude;
                    frequency *= 2d;
                    amplitude *= 0.5d;
                }

                _destinationV1[x + (y * Size)] = sum;
            }
        }

        return _destinationV1;
    }

    /// <summary>FastNoiseLite called once per sample: the realistic thing to beat.</summary>
    [Benchmark(Baseline = true, Description = "FastNoiseLite reference, per sample")]
    public float[] ReferenceLoop()
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                _destination[x + (y * Size)] = _reference.GetNoise(x, y);
            }
        }

        return _destination;
    }

    /// <summary>This library, scalar backend.</summary>
    [Benchmark(Description = "Tedd.FastNoise scalar")]
    public float[] Scalar()
    {
        _noise.Fill(_destination, _region, NoiseBackend.Scalar);
        return _destination;
    }

    /// <summary>This library, single-threaded SIMD.</summary>
    [Benchmark(Description = "Tedd.FastNoise SIMD")]
    public float[] Simd()
    {
        _noise.Fill(_destination, _region, NoiseBackend.Simd);
        return _destination;
    }

    /// <summary>This library, SIMD across all cores.</summary>
    [Benchmark(Description = "Tedd.FastNoise SIMD + parallel")]
    public float[] Parallel()
    {
        _noise.Fill(_destination, _region, NoiseBackend.Parallel);
        return _destination;
    }
}
