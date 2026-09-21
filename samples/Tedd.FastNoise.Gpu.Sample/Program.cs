// Demonstrates float fills, density terrain and composed planetary material graphs at three LODs.
// Readback and CPU projection exist only to inspect the packed result and save documentation images.
using System;
using System.IO;
using System.Linq;
using Tedd.FastNoise.Gallery;

namespace Tedd.FastNoise.Gpu.Sample;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string output = args.Length == 0 ? "artifacts/gpu-sample" : args[0];
            Directory.CreateDirectory(output);
            using var gpu = new HeadlessVulkan();
            Console.WriteLine($"Vulkan device: {gpu.DeviceName}");
            var noise = new NoiseGenerator(1337)
            {
                NoiseType = NoiseType.OpenSimplex2, FractalType = FractalType.FBm,
                Frequency = .01f, Octaves = 6, Lod = LodPolicy.Automatic
            };

            // Field output is X-fastest float32, unlike the Z-fastest brick payload below.
            var fieldRequest = noise.CreateRequest(new GridRegion2D(-128, -128, 256, 256));
            uint[] field = gpu.Run(cmd => gpu.Producer.RecordNoise(cmd, gpu.Output, fieldRequest), 256 * 256);
            var pixels = new byte[field.Length * 3];
            for (int i = 0; i < field.Length; i++)
            {
                float value = BitConverter.UInt32BitsToSingle(field[i]);
                byte shade = (byte)Math.Clamp((value + 1) * 127.5f, 0, 255);
                pixels[i * 3] = shade; pixels[i * 3 + 1] = shade; pixels[i * 3 + 2] = shade;
            }
            Png.Write(Path.Combine(output, "gpu-field.png"), 256, 256, pixels);

            // All LODs cover the same 256-world-unit cube. Only sampling resolution changes.
            foreach (int side in new[] { 32, 16, 8 })
            {
                float spacing = 256f / side;
                var request = noise.CreateRequest(new GridRegion3D(-128, -128, -128, side, side, side, spacing));
                var settings = new VoxelTerrainSettings(Height: 0, Amplitude: 180);
                int capacityWords = checked((int)(VoxelTerrainSettings.RequiredBytes(side) / 4));
                uint[] words = gpu.Run(cmd => gpu.Producer.RecordTerrain(cmd, gpu.Output, request, settings), capacityWords);
                int usedWords = checked((int)words[0] + 48);
                string name = $"gpu-terrain-{side}";
                using (var file = new BinaryWriter(File.Create(Path.Combine(output, name + ".bin"))))
                    for (int i = 0; i < usedWords; i++) file.Write(words[i]);

                float[] preview = DecodePreview(words, side, out int solidCells);
                var picture = VoxelRenderer.Render(preview, 32, 0);
                Png.Write(Path.Combine(output, name + ".png"), picture.Width, picture.Height, picture.Pixels);
                Console.WriteLine($"{side}^3, step {spacing}: {request.Octaves} octaves, " +
                    $"{solidCells} solid cells, {usedWords * 4:N0} used / {capacityWords * 4:N0} reserved bytes");
            }
            RunPlanetSample(gpu, output);
            Console.WriteLine($"Wrote field, terrain previews and packed payloads to {Path.GetFullPath(output)}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void RunPlanetSample(HeadlessVulkan gpu, string output)
    {
        var options = new PlanetOptions();
        foreach (int side in new[] { 32, 16, 8 })
        {
            float step = 256f / side;
            var (columns, voxels) = PlanetRecipe.Build(options, step);
            uint[] words = gpu.RunPlanet(columns, voxels, options, side, step);
            var column = new double[2];
            var expected = new double[1];
            var pixels = new byte[side * side * 3];
            int checkedCells = 0;
            for (int x = 0; x < side; x++) for (int z = 0; z < side; z++)
            {
                columns.Evaluate([-128 + (x + .5) * step, options.Radius, -128 + (z + .5) * step], column);
                uint topMaterial = 0;
                for (int y = 0; y < side; y++)
                {
                    voxels.Evaluate([column[0], column[1], -128 + (y + .5) * step], expected);
                    uint material = Material(words, side, x, y, z);
                    if (material != (uint)expected[0]) throw new InvalidOperationException($"Planet CPU/GPU mismatch at {side}: {x},{y},{z}.");
                    if (material != 0) topMaterial = material;
                    checkedCells++;
                }
                uint color = PlanetRecipe.Colors[topMaterial];
                int pixel = (z * side + x) * 3;
                pixels[pixel] = (byte)color; pixels[pixel + 1] = (byte)(color >> 8); pixels[pixel + 2] = (byte)(color >> 16);
            }
            string name = $"gpu-planet-{side}";
            Png.Write(Path.Combine(output, name + ".png"), side, side, pixels);
            using (var file = new BinaryWriter(File.Create(Path.Combine(output, name + ".bin"))))
                for (int i = 0; i < words[0] + PlanetRecipe.Colors.Length * 24; i++) file.Write(words[i]);
            File.WriteAllText(Path.Combine(output, name + ".comp"), VulkanGraphShaderCompiler.GenerateSource(columns));
            int noiseNodes = columns.Nodes.Count(n => n.Operation == Procedural.ProceduralOperation.Noise3D);
            Console.WriteLine($"Planet {side}^3: {checkedCells} CPU/GPU materials agree; {noiseNodes} live noise nodes; " +
                $"{columns.BuilderNodeCount} builder / {columns.Nodes.Count} live column nodes.");
        }
    }

    private static uint Material(uint[] words, int side, int x, int y, int z)
    {
        int bricks = side / 4;
        uint entry = words[4 + ((x >> 2) * bricks + (y >> 2)) * bricks + (z >> 2)];
        if (entry == 0) return 0;
        uint offset = entry & 0x7fffffffu;
        if ((entry & 0x80000000u) == 0) offset += (uint)(((x & 3) * 4 + (y & 3)) * 4 + (z & 3)) * 2;
        return words[offset] & 65535u;
    }

    private static float[] DecodePreview(uint[] words, int side, out int solidCells)
    {
        int brickSide = side / 4;
        var result = new float[32 * 32 * 32];
        solidCells = 0;
        // Nearest-cell expansion gives each LOD the same world-space framing in the preview.
        for (int x = 0; x < 32; x++) for (int y = 0; y < 32; y++) for (int z = 0; z < 32; z++)
        {
            int cx = x * side / 32, cy = y * side / 32, cz = z * side / 32;
            uint entry = words[4 + ((cx >> 2) * brickSide + (cy >> 2)) * brickSide + (cz >> 2)];
            uint offset = entry & 0x7fffffffu;
            if ((entry & 0x80000000u) == 0)
                offset += (uint)(((cx & 3) * 4 + (cy & 3)) * 4 + (cz & 3)) * 2;
            bool solid = entry != 0 && (words[offset] & 65535u) != 0;
            result[x + 32 * (y + 32 * z)] = solid ? .25f : -1;
            if (solid && x % (32 / side) == 0 && y % (32 / side) == 0 && z % (32 / side) == 0) solidCells++;
        }
        return result;
    }
}
