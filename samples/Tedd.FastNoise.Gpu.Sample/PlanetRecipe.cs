// Demonstrates application-owned planetary composition using only public FastNoise graph APIs.
// The recipe supplies layers and material rules; the library owns specialization, folding and execution.
using Tedd.FastNoise.Procedural;

namespace Tedd.FastNoise.Gpu.Sample;

internal sealed record PlanetOptions(int Seed = 1337, double Radius = 4096,
    double Relief = 160, double MountainHeight = 72, double DetailCutoff = 16,
    double SoilDepth = 12, double SnowHeight = 70);

internal static class PlanetRecipe
{
    internal static (CompiledProceduralGraph Columns, CompiledProceduralGraph Voxels) Build(PlanetOptions options, float step)
    {
        var g = new ProceduralGraph();
        var x = g.Convert(g.Input(0), ProceduralType.Float32);
        var y = g.Convert(g.Input(1), ProceduralType.Float32);
        var z = g.Convert(g.Input(2), ProceduralType.Float32);
        ProceduralValue Noise(int seed, float frequency, FractalType fractal) =>
            g.Convert(g.Noise3D(new NoiseGenerator(seed)
            {
                NoiseType = NoiseType.OpenSimplex2, Frequency = frequency,
                FractalType = fractal, Octaves = 5, Lod = LodPolicy.Automatic
            }, x, y, z, step), ProceduralType.Float64);

        var continents = Noise(options.Seed, .002f, FractalType.FBm);
        var mountains = Noise(options.Seed + 1, .008f, FractalType.Ridged);
        var land = g.SmoothStep(g.Constant(-.2), g.Constant(.3), continents);
        // Configuration arithmetic folds here; runtime terrain arithmetic retains its rounding order.
        var height = continents * g.Constant(options.Relief) + land * mountains * g.Constant(options.MountainHeight);
        var detail = Noise(options.Seed + 2, .03f, FractalType.FBm) * g.Constant(8d);
        height += g.Select(g.Constant(step <= options.DetailCutoff), detail, g.Constant(0d));
        var moisture = Noise(options.Seed + 3, .004f, FractalType.FBm);
        var columns = g.Compile(height, moisture);

        var v = new ProceduralGraph();
        var surface = v.Floor(v.Input(0));
        var wetness = v.Input(1);
        var elevation = v.Input(2);
        // Index zero is air; the host-defined palette is independent of a game's block registry.
        var surfaceMaterial = v.Select(v.GreaterThan(surface, v.Constant(options.SnowHeight)), v.Constant(4u),
            v.Select(v.LessThan(wetness, v.Constant(-.1)), v.Constant(3u), v.Constant(2u)));
        var landMaterial = v.Select(v.GreaterThan(elevation, surface - v.Constant(options.SoilDepth)), surfaceMaterial, v.Constant(1u));
        var material = v.Select(v.LessThanOrEqual(elevation, surface), landMaterial,
            v.Select(v.LessThanOrEqual(elevation, v.Constant(0d)), v.Constant(5u), v.Constant(0u)));
        return (columns, v.Compile(material));
    }

    internal static readonly uint[] Colors = [0, 0xff777777, 0xff488f52, 0xffbdc9d9, 0xfff4f0e8, 0xffc57a34];

    internal static uint[] Palette()
    {
        var palette = new uint[Colors.Length * 24];
        for (int material = 0; material < Colors.Length; material++)
            for (int face = 0; face < 6; face++) palette[material * 24 + face * 4] = Colors[material];
        return palette;
    }
}
