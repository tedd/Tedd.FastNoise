// Purpose: verify portable structure occupancy survives shader compilation and device execution.
// Responsibilities: compare solid and hollow box footprints across GPU and CPU samples at coarse spacing.
// Design intent: geometry is ordinary graph arithmetic; no device-only structure approximation is permitted.
using System;
using System.Runtime.InteropServices;
using Tedd.FastNoise.Procedural;

namespace Tedd.FastNoise.Gpu.Tests;

public sealed class ProceduralGeometryGpuTests
{
    [Fact]
    public void GeometryCompilesToSpirvWithExistingOperations()
    {
        var graph = CreateGraph();
        byte[] spirv = VulkanGraphShaderCompiler.Compile(graph);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv));
        Assert.Equal(0, spirv.Length % 4);
    }

    [GpuFact]
    public void CoarseSolidAndShellFootprintsMatchCpuOnDevice()
    {
        using var gpu = new GpuDevice(enableFloat64: true);
        var graph = CreateGraph();
        using var producer = new VulkanGraphProducer(gpu.Vk, gpu.PhysicalDevice, gpu.Device, graph, true);
        using var binding = producer.BindGridOutput(gpu.Buffer, 512 * 1024, gpu.OutputOffset);
        const int width = 13, height = 7, depth = 11, count = width * height * depth;
        uint[] words = gpu.Run(cmd => producer.RecordGrid(cmd, binding, -24, -8, -20, width, height, depth, 4), count * 4);
        double[] actual = MemoryMarshal.Cast<uint, double>(words).ToArray();
        double[] expected = new double[2];
        int occupied = 0, empty = 0;
        for (int i = 0; i < count; i++)
        {
            graph.Evaluate([-24 + i % width * 4, -8 + i / width % height * 4, -20 + i / (width * height) * 4], expected);
            Assert.Equal(expected[0], actual[i * 2]);
            Assert.Equal(expected[1], actual[i * 2 + 1]);
            if (expected[1] == 0) empty++; else occupied++;
        }
        Assert.True(occupied > 0 && empty > 0);
    }

    private static CompiledProceduralGraph CreateGraph()
    {
        var g = new ProceduralGraph();
        var x = g.Input(0); var y = g.Input(1); var z = g.Input(2);
        var minimum = (x, y, z);
        var maximum = (x + g.Constant(4d), y + g.Constant(4d), z + g.Constant(4d));
        (ProceduralValue, ProceduralValue, ProceduralValue) V(double a, double b, double c) =>
            (g.Constant(a), g.Constant(b), g.Constant(c));
        return g.Compile(g.IntersectsBox(minimum, maximum, V(-7, 0, -12), V(-6, 18, 12)),
            g.IntersectsBoxShell(minimum, maximum, V(-8, 0, -12), V(8, 20, 12), V(-7, 1, -11), V(7, 19, 11)));
    }
}
