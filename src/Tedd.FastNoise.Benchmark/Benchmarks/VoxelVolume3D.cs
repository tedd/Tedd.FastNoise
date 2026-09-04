using BenchmarkDotNet.Attributes;
using Tedd.FastNoise.Internal;

namespace Tedd.FastNoise.Benchmark.Benchmarks;

/// <summary>
/// The voxel case: a 3D density field for one chunk, which is what a Minecraft-like world actually
/// asks for and the reason this library exists.
/// </summary>
/// <remarks>
/// A 16x16x256 column is 65,536 samples, and a player walking at speed pulls a few of those per
/// second per direction. The question is what a chunk costs, and whether it is worth spending
/// cores on it or better to leave the cores for chunk-level parallelism.
/// </remarks>
[NoiseBenchmark]
[MemoryDiagnoser]
public class VoxelVolume3D
{
    private float[] _destination = [];
    private NoiseGenerator _noise = null!;
    private FastNoiseLiteCore _reference = null!;
    private GridRegion3D _region;

    /// <summary>Which algorithm the chunk is generated with.</summary>
    [Params(NoiseType.OpenSimplex2, NoiseType.Perlin, NoiseType.Value, NoiseType.Cellular)]
    public NoiseType Type { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // One 16x16x256 world column.
        _region = new GridRegion3D(0f, 0f, 0f, 16, 256, 16);
        _destination = new float[_region.SampleCount];

        _noise = new NoiseGenerator(1337)
        {
            NoiseType = Type,
            FractalType = FractalType.FBm,
            Octaves = 4,
            Frequency = 0.02f,
            ParallelThreshold = 1,
        };

        _reference = new FastNoiseLiteCore(1337);
        _reference.SetNoiseType((FastNoiseLiteCore.NoiseType)Type);
        _reference.SetFractalType(FastNoiseLiteCore.FractalType.FBm);
        _reference.SetFractalOctaves(4);
        _reference.SetFrequency(0.02f);
    }

    /// <summary>FastNoiseLite called once per voxel.</summary>
    [Benchmark(Baseline = true, Description = "FastNoiseLite reference, per sample")]
    public float[] ReferenceLoop()
    {
        int index = 0;

        for (int z = 0; z < _region.Depth; z++)
        {
            for (int y = 0; y < _region.Height; y++)
            {
                for (int x = 0; x < _region.Width; x++)
                {
                    _destination[index++] = _reference.GetNoise(x, y, z);
                }
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
