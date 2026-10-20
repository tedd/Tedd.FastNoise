// Frozen Forcecraft Earth noise/land benchmark recipe; no server or persistence dependencies.
// Source: Forcecraft10 a78ba56e, EarthProceduralGraph.cs SHA256
// 19cce27ffa1db23229682cfcd6416171c808a26d237d40a2aa37b3e19db9e656.
// Methods retain their original statements. This file is compiled for both implementations.
#if EARTH_BASELINE
extern alias baseline;
using baseline::Tedd.FastNoise;
using baseline::Tedd.FastNoise.Procedural;
namespace Tedd.FastNoise.Performance.BaselineRecipe;
#else
using Tedd.FastNoise;
using Tedd.FastNoise.Procedural;
namespace Tedd.FastNoise.Performance.CurrentRecipe;
#endif
using System;
using System.Linq;
public static class EarthRecipe
{
    public static CompiledProceduralGraph CreateNoise(int seed, ReliefOptions relief, double step)
    {
        var graph = new ProceduralGraph();
        return graph.Compile(Noise(graph, seed, relief, step, Coordinates(graph)));
    }

    /// <summary>Datum-world XYZ inputs; nine noise outputs followed by elevation and drainage valley.</summary>
    public static CompiledProceduralGraph CreateLand(int seed, RegionServerConfig config, double step)
    {
        var graph = new ProceduralGraph();
        var noise = Noise(graph, seed, config.Terrain.Relief, step, Coordinates(graph));
        var (elevation, valley) = Land(graph, noise, config);
        return graph.Compile([.. noise, elevation, valley]);
    }

    private static ProceduralValue[] Coordinates(ProceduralGraph graph) =>
        [graph.Input(0, ProceduralType.Float64), graph.Input(1, ProceduralType.Float64), graph.Input(2, ProceduralType.Float64)];

    private static ProceduralValue[] Noise(ProceduralGraph graph, int seed, ReliefOptions relief,
        double step, ProceduralValue[] coordinates)
    {
        ArgumentNullException.ThrowIfNull(relief);
        if (!double.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        var xyz = coordinates.Select(p => graph.Convert(p, ProceduralType.Float32)).ToArray();
        ProceduralValue Field(params NoiseLayer[] layers)
        {
            // Coarse voxel size changes density, not terrain identity at a sample point.
            var stack = new NoiseStack { Lod = LodPolicy.Disabled, ParallelThreshold = int.MaxValue };
            foreach (var layer in layers) stack.Add(layer);
            var value = graph.Stack3D(stack.Compile(), xyz[0], xyz[1], xyz[2], (float)step);
            // Normalization in the original sampler is float arithmetic followed by promotion.
            value = graph.Min(graph.Constant(1f), graph.Max(graph.Constant(0f),
                (value + graph.Constant(1f)) * graph.Constant(.5f)));
            return graph.Convert(value, ProceduralType.Float64);
        }
        return
        [
            Field(Layer(seed, .00065f, 3, .78f), Layer(unchecked(seed + 101), .0025f, 2, .22f)),
            Field(Layer(unchecked(seed + 10000), .0015f, 3, 1f)),
            Field(Layer(unchecked(seed + 20000), .013f, 3, .82f), Layer(unchecked(seed + 20101), .075f, 2, .18f)),
            Field(Layer(unchecked(seed + 30000), .003f, 4, 1f, FractalType.Ridged),
                Layer(unchecked(seed + 30101), .0008f, 2, .5f, blend: LayerBlend.Multiply, offset: .5f)),
            Field(Layer(unchecked(seed + 50000), .00075f, 3, 1f)),
            Field(Layer(unchecked(seed + 60000), .0012f, 3, 1f)),
            Field(Layer(unchecked(seed + 70000), .0018f, 1, .90f, offset: .21f),
                Layer(unchecked(seed + 70101), .009f, 2, .10f)),
            Field(Layer(unchecked(seed + 80000), (float)(1d / relief.RegionWavelengthBlocks), relief.RegionOctaves, 1f)),
            Field(Layer(unchecked(seed + 90000), (float)(1d / relief.RangeWavelengthBlocks), relief.RangeOctaves, 1f, FractalType.Ridged))
        ];
    }

    private static NoiseLayer Layer(int seed, float frequency, int octaves, float amplitude,
        FractalType fractal = FractalType.FBm, LayerBlend blend = LayerBlend.Add, float offset = 0f) => new()
    {
        Amplitude = amplitude, Offset = offset, Blend = blend, FeatureSize = 1f / frequency,
        Source = new NoiseGenerator(seed)
        {
            NoiseType = NoiseType.OpenSimplex2, FractalType = fractal, Frequency = frequency,
            Octaves = octaves, Gain = .5f, Lacunarity = 2f, Lod = LodPolicy.Disabled, ParallelThreshold = int.MaxValue
        }
    };

    private static (ProceduralValue Elevation, ProceduralValue Valley) Land(ProceduralGraph g,
        ProceduralValue[] n, RegionServerConfig config)
    {
        ProceduralValue C(double value) => g.Constant(value);
        var relief = config.Terrain.Relief;
        var erosion = C(1) - n[1];
        var ridges = g.Max(C(0), n[3] - C(.48));
        var region = (n[7] - C(.5)) * C(2);
        var rangeMask = SmoothStep(g, relief.RangeMaskStart01, relief.RangeMaskFull01, n[7]);
        var elevation = (n[0] - C(.35)) * C(config.TerrainBaseAmplitude) * C(2)
            + (n[2] - C(.5)) * C(config.TerrainGroundAmplitude) * C(.56)
            + ridges * C(config.TerrainMountainAmplitude) * C(1.4) * (C(.3) + erosion)
            + region * C(relief.RegionAmplitudeBlocks)
            + rangeMask * n[8] * n[8] * C(relief.RangeAmplitudeBlocks)
            - C(Math.Floor(config.TerrainBaseLevel + 4d) - config.TerrainBaseLevel);
        var valley = C(1) - SmoothStep(g, .025, .11, g.Abs(n[6] * C(2) - C(1)));
        valley *= C(1) - SmoothStep(g, 0, relief.RiverMaximumElevationBlocks, elevation);
        var riverBed = C(-5) + n[2] * C(1.2);
        elevation = g.Select(g.GreaterThan(elevation, riverBed), elevation + (riverBed - elevation) * valley, elevation);
        return (elevation, valley);
    }

    private static ProceduralValue SmoothStep(ProceduralGraph g, double from, double to, ProceduralValue value)
    {
        var t = g.Min(g.Constant(1d), g.Max(g.Constant(0d),
            (value - g.Constant(from)) / g.Constant(to - from)));
        return t * t * (g.Constant(3d) - g.Constant(2d) * t);
    }

}
// Only constants consumed by the frozen pure recipe are represented here.
public sealed class ReliefOptions
{
    public int RegionOctaves = 2, RangeOctaves = 3;
    public double RegionWavelengthBlocks = 12000, RangeWavelengthBlocks = 3200;
    public double RangeMaskStart01 = .48, RangeMaskFull01 = .72;
    public double RegionAmplitudeBlocks = 320, RangeAmplitudeBlocks = 1000;
    public double RiverMaximumElevationBlocks = 36;
}
public sealed class TerrainOptions { public ReliefOptions Relief = new(); }
public sealed class RegionServerConfig
{
    public TerrainOptions Terrain = new();
    public double TerrainBaseLevel = 48, TerrainBaseAmplitude = 24;
    public double TerrainGroundAmplitude = 40, TerrainMountainAmplitude = 190;
}

