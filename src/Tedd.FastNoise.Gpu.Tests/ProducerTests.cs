// Validates device arithmetic and the renderer's actual directory/cell/palette addressing contract.
// GPU tests are opt-in so ordinary CI does not mistake a CPU fallback for device coverage.
using System;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace Tedd.FastNoise.Gpu.Tests;

public sealed class GpuFactAttribute : FactAttribute
{
    public GpuFactAttribute()
    { if (Environment.GetEnvironmentVariable("FASTNOISE_GPU_TESTS") != "1") Skip = "Set FASTNOISE_GPU_TESTS=1 to execute Vulkan compute tests."; }
}

public sealed class ProducerTests(ITestOutputHelper log)
{
    [Fact]
    public void ShaderCompilesWithoutAVulkanDevice()
    {
        var code = VulkanNoiseProducer.CompileShader();
        Assert.Equal(0, code.Length % 4);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(code));
    }

    [GpuFact]
    public void InvalidRequestsCannotRecordWork()
    {
        using var gpu = new GpuDevice();
        var noise = new NoiseGenerator { NoiseType = NoiseType.Cellular };
        var request = noise.CreateRequest(new GridRegion3D(0, 0, 0, 8, 8, 8));
        Assert.Throws<NotSupportedException>(() => gpu.Producer.RecordNoise(default, gpu.Output, request));
        request = request with { NoiseType = NoiseType.Value, Frequency = float.NaN };
        Assert.Throws<ArgumentException>(() => gpu.Producer.RecordNoise(default, gpu.Output, request));
        request = request with { Frequency = float.MaxValue };
        Assert.Throws<ArgumentOutOfRangeException>(() => gpu.Producer.RecordNoise(default, gpu.Output, request));
        request = request with { Frequency = .01f, Region = new GridRegion3D(0, 0, 0, 8, 8, 9) };
        Assert.Throws<ArgumentException>(() => gpu.Producer.RecordTerrain(default, gpu.Output, request, new VoxelTerrainSettings()));
        request = request with { Region = new GridRegion3D(0, 0, 0, 128, 128, 128) };
        Assert.Throws<ArgumentException>(() => gpu.Producer.RecordTerrain(default, gpu.Output, request, new VoxelTerrainSettings()));
        request = request with { Region = new GridRegion3D(0, 0, 0, 8, 8, 8) };
        gpu.Output.Dispose();
        Assert.Throws<ObjectDisposedException>(() => gpu.Producer.RecordNoise(default, gpu.Output, request));
    }

    [GpuFact]
    public void DeviceFieldsAgreeWithScalarAcrossAlgorithmsFractalsRotationsAndLod()
    {
        using var gpu = new GpuDevice();
        log.WriteLine(gpu.DeviceName);
        foreach (var type in new[] { NoiseType.Value, NoiseType.Perlin, NoiseType.OpenSimplex2 })
        foreach (var fractal in new[] { FractalType.None, FractalType.FBm, FractalType.Ridged, FractalType.PingPong })
        foreach (var rotation in Enum.GetValues<RotationType3D>())
        foreach (int seed in new[] { -1337, int.MaxValue })
        foreach (float step in new[] { 0.5f, 1f, 8f, 64f })
        {
            var noise = new NoiseGenerator(seed)
            {
                NoiseType = type, FractalType = fractal, Octaves = 5, Frequency = .03125f,
                WeightedStrength = .37f, PingPongStrength = 2.1f, RotationType3D = rotation, Lod = LodPolicy.Automatic
            };
            var region = new GridRegion3D(-32, -7.25f, 13, 13, 9, 7, step);
            var request = noise.CreateRequest(region);
            var actual = gpu.Run(cmd => gpu.Producer.RecordNoise(cmd, gpu.Output, request), region.SampleCount);
            AssertBits(noise.Create(region, NoiseBackend.Scalar), actual, $"{type}/{fractal}/{rotation}/step={step}/3D");
            var region2 = new GridRegion2D(-32, -7.25f, 13, 9, step);
            var request2 = noise.CreateRequest(region2);
            actual = gpu.Run(cmd => gpu.Producer.RecordNoise(cmd, gpu.Output, request2), region2.SampleCount);
            AssertBits(noise.Create(region2, NoiseBackend.Scalar), actual, $"{type}/{fractal}/step={step}/2D");
        }
    }

    [GpuFact]
    public void TerrainDecodesLikeForcecraftAndReplacesPreviouslyOccupiedBricks()
    {
        using var gpu = new GpuDevice();
        foreach (int side in new[] { 8, 16, 32 })
        foreach (float step in new[] { 1f, 8f, 64f })
        foreach (float height in new[] { 1_000_000f, 0f, -1_000_000f })
        {
            var noise = new NoiseGenerator(987)
            { NoiseType = NoiseType.OpenSimplex2, FractalType = FractalType.FBm, Octaves = 5, Frequency = .01f, Lod = LodPolicy.Automatic };
            var region = new GridRegion3D(-17, -side * step / 2, 31, side, side, side, step);
            var request = noise.CreateRequest(region);
            var settings = new VoxelTerrainSettings(height, 48, 1, 0xff254667, 193);
            var words = gpu.Run(cmd => gpu.Producer.RecordTerrain(cmd, gpu.Output, request, settings), (int)(VoxelTerrainSettings.RequiredBytes(side) / 4));
            float[] values = noise.Create(region, NoiseBackend.Scalar);
            int bricks = side / 4, header = 4 + bricks * bricks * bricks;
            Assert.InRange(words[0], (uint)header, (uint)(words.Length - 48));
            Assert.Equal(0u, words[1]); Assert.Equal(0u, words[2]); Assert.Equal(0u, words[3]);
            var ranges = new List<(uint Offset, uint Count)>();
            for (int i = 4; i < header; i++)
            {
                uint entry = words[i];
                if (entry == 0) continue;
                uint offset = entry & 0x7fffffffu, count = (entry & 0x80000000u) != 0 ? 2u : 128u;
                Assert.True(offset >= header && offset + count <= words[0]);
                ranges.Add((offset, count));
            }
            ranges.Sort((a,b) => a.Offset.CompareTo(b.Offset));
            uint cursor = (uint)header;
            foreach (var range in ranges) { Assert.Equal(cursor, range.Offset); cursor += range.Count; }
            Assert.Equal(words[0], cursor);
            for (int x = 0; x < side; x++) for (int y = 0; y < side; y++) for (int z = 0; z < side; z++)
            {
                // The following addressing is the production voxel_trace.frag contract.
                uint entry = words[4 + ((x >> 2) * bricks + (y >> 2)) * bricks + (z >> 2)];
                uint offset = entry & 0x7fffffffu;
                if ((entry & 0x80000000u) == 0) offset += (uint)(((x & 3) * 4 + (y & 3)) * 4 + (z & 3)) * 2;
                uint shape = entry == 0 ? 0 : words[offset];
                uint light = entry == 0 ? 0 : words[offset + 1];
                float worldY = region.OriginY + y * step;
                bool solid = (values[x + side * (y + side * z)] * settings.Amplitude + settings.Height) - worldY * settings.VerticalScale > 0;
                Assert.Equal(solid ? 0xff000001u : 0u, shape);
                Assert.Equal(solid ? 0xffc10000u : 0u, light);
            }
            for (uint face = 0; face < 12; face++)
            {
                uint offset = words[0] + face * 4;
                Assert.Equal(face < 6 ? 0u : settings.Color, words[offset]);
                Assert.Equal(0u, words[offset + 1]); Assert.Equal(0u, words[offset + 2]); Assert.Equal(0u, words[offset + 3]);
            }
            if (height < -1000) Assert.Equal((uint)header, words[0]);
            if (height > 1000) Assert.Equal((uint)(header + bricks * bricks * bricks * 2), words[0]);
        }
    }

    [Fact]
    public void RequestsResolveLodAndRejectOverflow()
    {
        var noise = new NoiseGenerator { Frequency = .01f, Octaves = 8, Lod = LodPolicy.Automatic };
        var r = noise.CreateRequest(new GridRegion3D(0, 0, 0, 8, 8, 8, 64));
        Assert.Equal(noise.Lod.Resolve(noise.Frequency, noise.Lacunarity, noise.Octaves, 64), (r.Octaves, r.LastOctaveFade));
        Assert.Throws<OverflowException>(() => noise.CreateRequest(new GridRegion3D(0, 0, 0, 65536, 65536, 2)));
        Assert.False(VulkanNoiseProducer.Supports(NoiseType.Cellular, FractalType.None));
        Assert.False(VulkanNoiseProducer.Supports(NoiseType.Value, FractalType.DomainWarpIndependent));
        Assert.Throws<ArgumentOutOfRangeException>(() => VoxelTerrainSettings.RequiredBytes(12));
    }

    private static void AssertBits(float[] expected, uint[] actual, string context)
    {
        for (int i = 0; i < expected.Length; i++)
            Assert.True(BitConverter.SingleToUInt32Bits(expected[i]) == actual[i],
                $"{context} at {i}: CPU {expected[i]:R} (0x{BitConverter.SingleToUInt32Bits(expected[i]):x8}), GPU {BitConverter.UInt32BitsToSingle(actual[i]):R} (0x{actual[i]:x8})");
    }
}
