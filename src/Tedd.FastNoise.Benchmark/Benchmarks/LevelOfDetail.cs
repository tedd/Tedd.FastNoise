using BenchmarkDotNet.Attributes;

namespace Tedd.FastNoise.Benchmark.Benchmarks;

/// <summary>
/// What does level-of-detail culling actually save?
/// </summary>
/// <remarks>
/// <para>
/// The claim is that a distant view costs a fraction of a close one, because the octaves and
/// layers that describe fine detail are dropped rather than evaluated and aliased. This measures
/// the same tile at the same output resolution, sampled at four world-space spacings, with the
/// policy on and off.
/// </para>
/// <para>
/// The "off" rows are the control: they do the full work at every spacing, which is exactly the
/// waste the feature exists to remove.
/// </para>
/// </remarks>
[NoiseBenchmark]
[MemoryDiagnoser]
public class LevelOfDetail
{
    private CompiledNoiseStack _full = null!;
    private CompiledNoiseStack _culled = null!;
    private float[] _destination = [];

    private const int TileSize = 256;

    /// <summary>World units between samples. 1 is a player on the ground; 4096 is a view from orbit.</summary>
    [Params(1f, 64f, 1024f, 16384f)]
    public float Step { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _destination = new float[TileSize * TileSize];
        _full = BuildStack(LodPolicy.Disabled);
        _culled = BuildStack(LodPolicy.Automatic with { CullLayers = true });
    }

    /// <summary>A five-layer world, coarse to fine, of the shape a planet generator would use.</summary>
    private static CompiledNoiseStack BuildStack(LodPolicy lod)
    {
        NoiseStack stack = new() { Lod = lod, ParallelThreshold = int.MaxValue };

        stack.Add(new NoiseLayer
        {
            Source = new NoiseGenerator(1) { Frequency = 0.00005f, FractalType = FractalType.FBm, Octaves = 4 },
            Amplitude = 1f,
            FeatureSize = 20000f,
            Name = "continents",
        });

        stack.Add(new NoiseLayer
        {
            Source = new NoiseGenerator(2) { Frequency = 0.0005f, FractalType = FractalType.Ridged, Octaves = 5 },
            Amplitude = 0.5f,
            FeatureSize = 2000f,
            Name = "mountains",
        });

        stack.Add(new NoiseLayer
        {
            Source = new NoiseGenerator(3) { Frequency = 0.005f, FractalType = FractalType.FBm, Octaves = 4 },
            Amplitude = 0.2f,
            FeatureSize = 200f,
            Name = "hills",
        });

        stack.Add(new NoiseLayer
        {
            Source = new NoiseGenerator(4) { Frequency = 0.05f, NoiseType = NoiseType.Cellular },
            Amplitude = 0.05f,
            FeatureSize = 20f,
            Name = "rocks",
        });

        stack.Add(new NoiseLayer
        {
            Source = new NoiseGenerator(5) { Frequency = 0.5f, NoiseType = NoiseType.Value },
            Amplitude = 0.01f,
            FeatureSize = 2f,
            Name = "surface grain",
        });

        return stack.Compile();
    }

    /// <summary>Every octave of every layer, whatever the sample spacing.</summary>
    [Benchmark(Baseline = true, Description = "Full detail (LOD off)")]
    public float[] FullDetail()
    {
        _full.Fill(_destination, new GridRegion2D(0f, 0f, TileSize, TileSize, Step), NoiseBackend.Simd);
        return _destination;
    }

    /// <summary>Only the octaves and layers the sample grid can carry.</summary>
    [Benchmark(Description = "Band-limited (LOD on)")]
    public float[] BandLimited()
    {
        _culled.Fill(_destination, new GridRegion2D(0f, 0f, TileSize, TileSize, Step), NoiseBackend.Simd);
        return _destination;
    }
}
