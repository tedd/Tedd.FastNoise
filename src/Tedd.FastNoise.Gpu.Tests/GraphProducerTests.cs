// Exercises specialized shader generation and real-device graph/terrain parity.
// Tests cover mixed precision, exceptional conversions, all cube-face orientations and
// independently decoded packed material/light cells rather than matching implementation text.
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Tedd.FastNoise.Procedural;
using Xunit.Abstractions;

namespace Tedd.FastNoise.Gpu.Tests;

public sealed class GraphProducerTests(ITestOutputHelper log)
{
    private const ulong InputOffset = 2 * 1024 * 1024;
    private const ulong ScratchOffset = 1024 * 1024;
    private const ulong BufferRange = 512 * 1024;

    [Fact]
    public void SpecializedGraphCompilesAndUnsupportedDoublePowerFailsExplicitly()
    {
        var g = new ProceduralGraph();
        var x = g.Input(0);
        var noise = g.Noise2D(new NoiseGenerator(213) { FractalType = FractalType.FBm, Octaves = 3 }, g.Convert(x, ProceduralType.Float32), g.Input(1, ProceduralType.Float32));
        var graph = g.Compile(g.Convert(noise, ProceduralType.Float64) * g.Constant(12d) + x);
        var spirv = VulkanGraphShaderCompiler.Compile(graph);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv));
        Assert.Equal(0, spirv.Length % 4);
        spirv[0] ^= 255;
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(VulkanGraphShaderCompiler.Compile(graph)));
        Assert.Contains("precise double", VulkanGraphShaderCompiler.GenerateSource(graph));
        Assert.Throws<NotSupportedException>(() => VulkanGraphShaderCompiler.Compile(g.Compile(g.Pow(x, g.Constant(1.5d)))));
    }

    [GpuFact]
    public void GraphBindingsValidateCapacityOwnershipAndEnabledDeviceFeatures()
    {
        using var gpu = new GpuDevice(enableFloat64: true);
        var g = new ProceduralGraph(); var graph = g.Compile(g.Input(0));
        Assert.Throws<NotSupportedException>(() => new VulkanGraphProducer(gpu.Vk, gpu.PhysicalDevice, gpu.Device, graph, false));
        using var producer = new VulkanGraphProducer(gpu.Vk, gpu.PhysicalDevice, gpu.Device, graph, true);
        using var other = new VulkanGraphProducer(gpu.Vk, gpu.PhysicalDevice, gpu.Device, graph, true);
        using var binding = producer.BindGridOutput(gpu.Buffer, 8, gpu.OutputOffset);
        Assert.Throws<ArgumentException>(() => producer.RecordGrid(default, binding, 0, 0, 0, 2, 1, 1));
        Assert.Throws<ArgumentException>(() => other.RecordGrid(default, binding, 0, 0, 0, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => producer.Bind(gpu.Buffer, 64, gpu.Buffer, 64, gpu.OutputOffset, gpu.OutputOffset));
        Assert.Throws<ArgumentOutOfRangeException>(() => producer.RecordGrid(default, binding, 0, 0, 0, 1, 1, 1, 0));
        binding.Dispose();
        Assert.Throws<ObjectDisposedException>(() => producer.RecordGrid(default, binding, 0, 0, 0, 1, 1, 1));
    }

    [GpuFact]
    public void MixedPrecisionGridMatchesCpuNoiseAndOrderedMath()
    {
        using var gpu = new GpuDevice(enableFloat64: true);
        log.WriteLine(gpu.DeviceName);
        foreach (var type in new[] { NoiseType.Value, NoiseType.Perlin, NoiseType.OpenSimplex2 })
        foreach (var fractal in new[] { FractalType.None, FractalType.FBm, FractalType.Ridged, FractalType.PingPong })
        {
            var g = new ProceduralGraph();
            var x = g.Input(0); var y = g.Input(1); var z = g.Input(2);
            var noise = g.Noise3D(new NoiseGenerator(-2137) { NoiseType = type, FractalType = fractal, Octaves = 4,
                Frequency = .03125f, WeightedStrength = .37f, PingPongStrength = 2.1f }, g.Convert(x, ProceduralType.Float32), g.Convert(y, ProceduralType.Float32), g.Convert(z, ProceduralType.Float32));
            var height = g.Convert(noise, ProceduralType.Float64) * g.Constant(12.25d) + x / g.Constant(3.0d);
            var graph = g.Compile(height, g.Select(g.LessThan(y, height), g.Constant(7u), g.Constant(0u)), noise);
            using var producer = new VulkanGraphProducer(gpu.Vk, gpu.PhysicalDevice, gpu.Device, graph, true);
            using var binding = producer.BindGridOutput(gpu.Buffer, BufferRange, gpu.OutputOffset);
            const int width = 13, heightCount = 5, depth = 7, count = width * heightCount * depth;
            uint[] words = gpu.Run(cmd => producer.RecordGrid(cmd, binding, -17, -3.25, 11, width, heightCount, depth, .5), count * 6);
            double[] actual = MemoryMarshal.Cast<uint, double>(words).ToArray();
            double[] input = new double[3], expected = new double[3];
            for (int i = 0; i < count; i++)
            {
                input[0] = -17 + (i % width) * .5; input[1] = -3.25 + ((i / width) % heightCount) * .5; input[2] = 11 + (i / (width * heightCount)) * .5;
                graph.Evaluate(input, expected);
                for (int o = 0; o < 3; o++) Assert.Equal(BitConverter.DoubleToInt64Bits(expected[o]), BitConverter.DoubleToInt64Bits(actual[i * 3 + o]));
            }
        }
    }

    [GpuFact]
    public void ExceptionalValuesConversionsHashesAndLookupMatchCpu()
    {
        using var gpu = new GpuDevice(enableFloat64: true);
        var g = new ProceduralGraph();
        var a = g.Input(0); var b = g.Input(1);
        var integer = g.Convert(a, ProceduralType.Int32); var unsigned = g.Convert(a, ProceduralType.UInt32);
        var graph = g.Compile(g.Min(a, b), g.Max(a, b), integer, unsigned, g.Hash(unsigned),
            g.Lookup(new double[] { -15, 17, 43 }, integer, ProceduralType.Float64), g.Constant(double.NegativeInfinity),
            g.Constant(double.PositiveInfinity), g.Constant(double.NaN));
        double[] inputs = [0.0, -0.0, -0.0, 0.0, double.NaN, 3, 5, double.NaN,
            double.PositiveInfinity, 1, double.NegativeInfinity, 1, 4294967296d, 17, -2147483649d, 17, 1.9, 7];
        gpu.WriteDoubles(InputOffset, inputs);
        using var producer = new VulkanGraphProducer(gpu.Vk, gpu.PhysicalDevice, gpu.Device, graph, true);
        using var binding = producer.Bind(gpu.Buffer, BufferRange, gpu.Buffer, BufferRange, InputOffset, gpu.OutputOffset);
        int count = inputs.Length / 2;
        uint[] words = gpu.Run(cmd => producer.Record(cmd, binding, count), count * graph.OutputCount * 2);
        double[] actual = MemoryMarshal.Cast<uint, double>(words).ToArray();
        var expected = new double[actual.Length]; graph.Fill(inputs, expected, count);
        for (int i = 0; i < expected.Length; i++)
        {
            if (double.IsNaN(expected[i])) Assert.True(double.IsNaN(actual[i]));
            else Assert.Equal(BitConverter.DoubleToInt64Bits(expected[i]), BitConverter.DoubleToInt64Bits(actual[i]));
        }
    }

    [GpuFact]
    public void RawSkyHaloMatchesSevenSampleSurfaceExposureIncludingVolumeBoundaries()
    {
        using var gpu = new GpuDevice(enableFloat64: true);
        const int side = 8, pitch = side + 2;
        var raw = Enumerable.Range(0, pitch * pitch * pitch).Select(i => unchecked((byte)(i * 37 + i / 100 * 11))).ToArray();
        gpu.WriteBytes(InputOffset, raw);
        var c = new ProceduralGraph(); var columns = c.Compile(c.Constant(0d));
        var v = new ProceduralGraph(); var voxels = v.Compile(v.Constant(1d));
        using var producer = new VulkanGraphTerrainProducer(gpu.Vk, gpu.PhysicalDevice, gpu.Device, columns, voxels,
            1, 1, 10, new uint[48], true);
        using var binding = producer.Bind(gpu.Buffer, BufferRange, gpu.Buffer, BufferRange, gpu.Buffer, (ulong)raw.Length,
            ScratchOffset, gpu.OutputOffset, InputOffset, skyIncludesHalo: true);
        using var shortSky = producer.Bind(gpu.Buffer, BufferRange, gpu.Buffer, BufferRange, gpu.Buffer, side * side * side,
            ScratchOffset, gpu.OutputOffset, InputOffset, skyIncludesHalo: true);
        Assert.Throws<ArgumentException>(() => producer.Record(default, shortSky, 0, 0, 0, side));
        var words = gpu.Run(cmd => producer.Record(cmd, binding, 0, 0, 0, side), (int)producer.RequiredBytes(side) / 4);
        for (int x = 0; x < side; x++) for (int y = 0; y < side; y++) for (int z = 0; z < side; z++)
        {
            int center = ((x + 1) * pitch + y + 1) * pitch + z + 1;
            byte exposure = raw[center];
            foreach (int delta in new[] { -1, 1, -pitch, pitch, -pitch * pitch, pitch * pitch }) exposure = Math.Max(exposure, raw[center + delta]);
            uint entry = words[4 + ((x >> 2) * (side / 4) + (y >> 2)) * (side / 4) + (z >> 2)];
            uint offset = entry & 0x7fffffffu;
            if ((entry & 0x80000000u) == 0) offset += (uint)(((x & 3) * 4 + (y & 3)) * 4 + (z & 3)) * 2;
            Assert.Equal(0xff000001u, words[offset]);
            Assert.Equal(0xff000000u | ((uint)exposure << 16), words[offset + 1]);
        }
    }

    [GpuFact]
    public void TwoStageTerrainPreservesFaceOrientationPaletteAndNonuniformSkylight()
    {
        using var gpu = new GpuDevice(enableFloat64: true);
        const int side = 8;
        var palette = Enumerable.Range(0, 72).Select(i => (uint)(i * 177)).ToArray();
        var sky = Enumerable.Range(0, side * side * side).Select(i => (byte)(i * 31)).ToArray();
        gpu.WriteBytes(InputOffset, sky);
        foreach (int axis in new[] { 0, 1, 2 }) foreach (int sign in new[] { -1, 1 })
        {
            var c = new ProceduralGraph();
            var noise = c.Noise3D(new NoiseGenerator(17) { NoiseType = NoiseType.Perlin, Frequency = .125f }, c.Input(0, ProceduralType.Float32), c.Input(1, ProceduralType.Float32), c.Input(2, ProceduralType.Float32));
            var columns = c.Compile(c.Convert(noise, ProceduralType.Float64) * c.Constant(3d));
            var v = new ProceduralGraph(); var h = v.Input(0); var elevation = v.Input(1);
            var voxels = v.Compile(v.Select(v.LessThan(elevation, h), v.Constant(1d),
                v.Select(v.LessThan(elevation, h + v.Constant(2d)), v.Constant(2d), v.Constant(0d))));
            using var producer = new VulkanGraphTerrainProducer(gpu.Vk, gpu.PhysicalDevice, gpu.Device, columns, voxels, axis, sign, 10, palette, true);
            using var binding = producer.Bind(gpu.Buffer, BufferRange, gpu.Buffer, BufferRange, gpu.Buffer, BufferRange,
                ScratchOffset, gpu.OutputOffset, InputOffset);
            double[] origin = [-3.5, -3.5, -3.5]; origin[axis] = sign * 10 - 3.5;
            uint[] words = gpu.Run(cmd => producer.Record(cmd, binding, origin[0], origin[1], origin[2], side, 1), (int)(producer.RequiredBytes(side) / 4));
            var point = new double[3]; var column = new double[1]; var voxel = new double[2]; var expected = new double[1];
            for (int x = 0; x < side; x++) for (int y = 0; y < side; y++) for (int z = 0; z < side; z++)
            {
                point[0] = origin[0] + x; point[1] = origin[1] + y; point[2] = origin[2] + z;
                double e = sign * point[axis] - 10; point[axis] = sign * 10;
                columns.Evaluate(point, column); voxel[0] = column[0]; voxel[1] = e; voxels.Evaluate(voxel, expected);
                uint material = (uint)expected[0]; int bricks = side / 4;
                uint entry = words[4 + ((x >> 2) * bricks + (y >> 2)) * bricks + (z >> 2)];
                uint offset = entry & 0x7fffffffu;
                if ((entry & 0x80000000u) == 0) offset += (uint)(((x & 3) * 4 + (y & 3)) * 4 + (z & 3)) * 2;
                uint shape = entry == 0 ? 0 : words[offset], light = entry == 0 ? 0 : words[offset + 1];
                Assert.Equal(material == 0 ? 0u : 0xff000000u | material, shape);
                Assert.Equal(material == 0 ? 0u : 0xff000000u | (uint)sky[(x * side + y) * side + z] << 16, light);
            }
            for (int i = 0; i < palette.Length; i++) Assert.Equal(palette[i], words[words[0] + i]);
            Assert.Equal(0u, words[1]); Assert.Equal(0u, words[2]); Assert.Equal(0u, words[3]);
        }
    }
}
