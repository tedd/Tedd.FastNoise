// Owns compute pipelines and descriptors on a caller-owned Vulkan device.
// Commands target caller-owned storage buffers so terrain can remain resident through rendering.
using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;
using Tedd.FastNoise.Internal;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tedd.FastNoise.Gpu;

/// <summary>Records Vulkan compute work without submitting, allocating voxel buffers, or reading back.</summary>
/// <remarks>All calls require external synchronization. The device must belong to physicalDevice.
/// Command buffers must execute on a compute-capable queue. Keep this producer, its outputs and
/// their buffers alive until submitted work completes. Synchronize prior readers/writers before reuse;
/// queue-family ownership transfers and cross-queue semaphores belong to the caller.</remarks>
public sealed unsafe class VulkanNoiseProducer : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDeviceLimits _limits;
    private DescriptorSetLayout _setLayout;
    private DescriptorPool _pool;
    private PipelineLayout _layout;
    private Pipeline _pipeline;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public Vector4 OriginStep;
        public uint Width, Height, Depth, Mode;
        public int Seed, Noise, Fractal, Octaves;
        public Vector4 FractalParameters, Shaping, Terrain;
        public uint Color, Sky, Rotation, Reserved;
    }

    /// <summary>Compiles the shader and creates a pipeline on an existing device.</summary>
    public VulkanNoiseProducer(Vk vk, PhysicalDevice physicalDevice, Device device, uint maximumOutputs = 1024)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentOutOfRangeException.ThrowIfZero(maximumOutputs);
        _vk = vk;
        _device = device;
        vk.GetPhysicalDeviceProperties(physicalDevice, out var properties);
        _limits = properties.Limits;
        try
        {
            var binding = new DescriptorSetLayoutBinding(0, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
            var setInfo = new DescriptorSetLayoutCreateInfo
            { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 1, PBindings = &binding };
            Check(vk.CreateDescriptorSetLayout(device, in setInfo, null, out _setLayout));
            var poolSize = new DescriptorPoolSize(DescriptorType.StorageBuffer, maximumOutputs);
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo, MaxSets = maximumOutputs,
                Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit, PoolSizeCount = 1, PPoolSizes = &poolSize
            };
            Check(vk.CreateDescriptorPool(device, in poolInfo, null, out _pool));
            var setLayout = _setLayout;
            var range = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Parameters));
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &setLayout,
                PushConstantRangeCount = 1, PPushConstantRanges = &range
            };
            Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out _layout));
            byte[] spirv = CompileShader();
            fixed (byte* code = spirv)
            {
                var moduleInfo = new ShaderModuleCreateInfo
                { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)code };
                Check(vk.CreateShaderModule(device, in moduleInfo, null, out var module));
                try
                {
                    byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
                    var stage = new PipelineShaderStageCreateInfo
                    { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit, Module = module, PName = entry };
                    var pipelineInfo = new ComputePipelineCreateInfo
                    { SType = StructureType.ComputePipelineCreateInfo, Stage = stage, Layout = _layout };
                    Check(vk.CreateComputePipelines(device, default, 1, in pipelineInfo, null, out _pipeline));
                }
                finally { vk.DestroyShaderModule(device, module, null); }
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Whether the shader implements this algorithm and fractal. Unsupported requests throw before recording.</summary>
    public static bool Supports(NoiseType noise, FractalType fractal) =>
        noise is NoiseType.OpenSimplex2 or NoiseType.Perlin or NoiseType.Value
        && fractal is FractalType.None or FractalType.FBm or FractalType.Ridged or FractalType.PingPong;

    /// <summary>Binds a buffer range. The buffer requires StorageBuffer usage; terrain also requires TransferDst.</summary>
    /// <remarks>The supplied capacity must fit the actual allocation. Offset must meet the device's storage-buffer
    /// alignment. Do not dispose the result until every command referencing it has completed.</remarks>
    public Output BindOutput(Buffer buffer, ulong capacityBytes, ulong offsetBytes = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.Handle == 0 || capacityBytes == 0 || capacityBytes % 4 != 0
            || capacityBytes > _limits.MaxStorageBufferRange
            || offsetBytes % Math.Max(4ul, _limits.MinStorageBufferOffsetAlignment) != 0
            || offsetBytes > ulong.MaxValue - capacityBytes)
            throw new ArgumentException("Invalid buffer range or storage-buffer alignment/capacity.");
        var layout = _setLayout;
        var allocate = new DescriptorSetAllocateInfo
        { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = _pool, DescriptorSetCount = 1, PSetLayouts = &layout };
        Check(_vk.AllocateDescriptorSets(_device, in allocate, out var set));
        var info = new DescriptorBufferInfo(buffer, offsetBytes, capacityBytes);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 0, DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &info
        };
        _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
        return new Output(this, buffer, set, offsetBytes, capacityBytes);
    }

    /// <summary>Records a 2D float32 fill in X-fastest order, followed by a shader-read memory barrier.</summary>
    public void RecordNoise(CommandBuffer commands, Output output, in NoiseFillRequest2D request)
    {
        var r = request;
        var three = new NoiseFillRequest3D
        {
            Seed = r.Seed, Frequency = r.Frequency, NoiseType = r.NoiseType, RotationType3D = RotationType3D.None,
            FractalType = r.FractalType, Octaves = r.Octaves, Lacunarity = r.Lacunarity, Gain = r.Gain,
            WeightedStrength = r.WeightedStrength, PingPongStrength = r.PingPongStrength,
            FractalBounding = r.FractalBounding, LastOctaveFade = r.LastOctaveFade,
            Region = new(r.Region.OriginX, r.Region.OriginY, 0, r.Region.Width, r.Region.Height, 1, r.Region.Step)
        };
        RecordField(commands, output, three, 0);
    }

    /// <summary>Records a 3D float32 fill in X-fastest, then Y, then Z order.</summary>
    public void RecordNoise(CommandBuffer commands, Output output, in NoiseFillRequest3D request) => RecordField(commands, output, request, 1);

    private void RecordField(CommandBuffer commands, Output output, in NoiseFillRequest3D request, uint mode)
    {
        var p = BuildParameters(request, mode);
        ValidateOutput(output, checked((ulong)request.Region.SampleCount * 4));
        Dispatch(commands, output, ref p, (p.Width + 3) / 4, (p.Height + 3) / 4, (p.Depth + 3) / 4);
        ShaderReadBarrier(commands);
    }

    /// <summary>Records density evaluation, brick compaction and palette generation into a Forcecraft-compatible buffer.</summary>
    /// <remarks>The region must be cubic with a power-of-two side from 8 to 256. Step is world units per voxel;
    /// pass a LOD-resolved request from NoiseGenerator.CreateRequest. The output uses the production brick
    /// format, with no thermal, emission or sloped-surface streams. Actual used bytes are (word[0] + 48) * 4.
    /// Atomic brick allocation makes byte offsets nondeterministic; decoded cells are deterministic on a fixed device.
    /// Final visibility covers subsequent shader reads on the same queue, including fragment buffer references.</remarks>
    public void RecordTerrain(CommandBuffer commands, Output output, in NoiseFillRequest3D request, in VoxelTerrainSettings settings)
    {
        var p = BuildParameters(request, 2);
        if (p.Width != p.Height || p.Width != p.Depth) throw new ArgumentException("Terrain regions must be cubic.", nameof(request));
        ValidateOutput(output, VoxelTerrainSettings.RequiredBytes((int)p.Width));
        settings.Validate();
        p.Terrain = new(settings.Height, settings.Amplitude, settings.VerticalScale, 0);
        p.Color = settings.Color;
        p.Sky = settings.Sky;
        uint bricks = p.Width / 4;
        uint headerWords = 4 + bricks * bricks * bricks;
        uint* header = stackalloc uint[] { headerWords, 0, 0, 0 };
        _vk.CmdUpdateBuffer(commands, output.Buffer, output.Offset, 16, header);
        Barrier(commands, PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit,
            PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
        Dispatch(commands, output, ref p, bricks, bricks, bricks);
        Barrier(commands, PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
        p.Mode = 3;
        Dispatch(commands, output, ref p, 1, 1, 1);
        ShaderReadBarrier(commands);
    }

    private static Parameters BuildParameters(in NoiseFillRequest3D r, uint mode)
    {
        var g = r.Region;
        g.Validate(checked(g.Width * g.Height * g.Depth));
        if (!Supports(r.NoiseType, r.FractalType)) throw new NotSupportedException("GPU generation supports OpenSimplex2, Perlin and Value with None, FBm, Ridged or PingPong fractals.");
        if (!Enum.IsDefined(r.RotationType3D) || r.Octaves is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(r), "Invalid rotation or octave count (1-128).");
        ReadOnlySpan<float> values = [g.OriginX, g.OriginY, g.OriginZ, r.Frequency, r.Lacunarity,
            r.Gain, r.FractalBounding, r.WeightedStrength, r.PingPongStrength, r.LastOctaveFade];
        foreach (float value in values)
            if (!float.IsFinite(value)) throw new ArgumentException("GPU request parameters must be finite.", nameof(r));
        if (r.LastOctaveFade is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(r), "Octave fade must be in [0,1].");
        // Float-to-int conversions outside the lattice's int32 range are undefined in SPIR-V.
        // Bound every octave conservatively, including the coordinate rotation/skew.
        double maxWorld = Math.Max(AxisBound(g.OriginX, g.Width, g.Step),
            Math.Max(AxisBound(g.OriginY, g.Height, g.Step), AxisBound(g.OriginZ, g.Depth, g.Step)));
        double lattice = maxWorld * Math.Abs((double)r.Frequency) * 3;
        int octaves = r.FractalType == FractalType.None ? 1 : r.Octaves;
        for (int i = 0; i < octaves; i++)
        {
            if (!double.IsFinite(lattice) || lattice > 2_000_000_000)
                throw new ArgumentOutOfRangeException(nameof(r), "Noise coordinates exceed the GPU int32 lattice range.");
            lattice *= Math.Abs((double)r.Lacunarity);
        }
        if (r.FractalType == FractalType.PingPong && Math.Abs(r.PingPongStrength) > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(r), "PingPongStrength exceeds the GPU folding range.");
        return new Parameters
        {
            OriginStep = new(g.OriginX, g.OriginY, g.OriginZ, g.Step),
            Width = (uint)g.Width, Height = (uint)g.Height, Depth = (uint)g.Depth, Mode = mode,
            Seed = r.Seed, Noise = (int)r.NoiseType, Fractal = (int)r.FractalType, Octaves = r.Octaves,
            FractalParameters = new(r.Frequency, r.Lacunarity, r.Gain, r.FractalBounding),
            Shaping = new(r.WeightedStrength, r.PingPongStrength, r.LastOctaveFade, 0), Rotation = (uint)r.RotationType3D
        };
    }

    private static double AxisBound(float origin, int count, float step)
    {
        double distance = (count - 1d) * step;
        double bound = Math.Max(Math.Abs(origin), Math.Abs(origin + distance));
        if (distance > float.MaxValue || bound > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(origin), "World coordinates overflow float32.");
        return bound;
    }

    private void ValidateOutput(Output output, ulong required)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        ObjectDisposedException.ThrowIf(output.IsDisposed, output);
        if (output.Owner != this || output.CapacityBytes < required)
            throw new ArgumentException("Output belongs to another producer or is too small.", nameof(output));
    }

    private void Dispatch(CommandBuffer commands, Output output, ref Parameters p, uint x, uint y, uint z)
    {
        if (x > _limits.MaxComputeWorkGroupCount[0] || y > _limits.MaxComputeWorkGroupCount[1] || z > _limits.MaxComputeWorkGroupCount[2])
            throw new ArgumentOutOfRangeException(nameof(p), "Dispatch exceeds device workgroup limits.");
        _vk.CmdBindPipeline(commands, PipelineBindPoint.Compute, _pipeline);
        var set = output.Set;
        _vk.CmdBindDescriptorSets(commands, PipelineBindPoint.Compute, _layout, 0, 1, in set, 0, null);
        fixed (Parameters* data = &p) _vk.CmdPushConstants(commands, _layout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Parameters), data);
        _vk.CmdDispatch(commands, x, y, z);
    }

    private void ShaderReadBarrier(CommandBuffer commands) => Barrier(commands,
        // Header words 1-3 are transfer-written and never touched by compute.
        PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit,
        AccessFlags.ShaderWriteBit | AccessFlags.TransferWriteBit,
        PipelineStageFlags.AllCommandsBit, AccessFlags.ShaderReadBit | AccessFlags.TransferReadBit);

    private void Barrier(CommandBuffer commands, PipelineStageFlags source, AccessFlags writes, PipelineStageFlags target, AccessFlags reads)
    {
        var barrier = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = writes, DstAccessMask = reads };
        _vk.CmdPipelineBarrier(commands, source, target, 0, 1, in barrier, 0, null, 0, null);
    }

    internal static byte[] CompileShader()
    {
        string Read(string name)
        {
            using var stream = typeof(VulkanNoiseProducer).Assembly.GetManifestResourceStream("Tedd.FastNoise.Gpu.Shaders." + name + ".glsl")!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var source = new StringBuilder("#version 450\n");
        AppendTable(source, "gradients2", Tables.Gradients2D);
        AppendTable(source, "gradients3", Tables.Gradients3D);
        // C# rounds each float constant expression; GLSL may fold the same expression at
        // higher precision. Transfer the CPU's rounded constants, not the algebraic formula.
        const float sqrt3 = 1.7320508075688772935274463415059f;
        const float g2 = (3 - sqrt3) / 6;
        const float c1 = 2 * (1 - 2 * g2) * (1 / g2 - 2);
        const float c2 = -2 * (1 - 2 * g2) * (1 - 2 * g2);
        source.Append("#define CPU_G2 ").Append(g2.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        source.Append("#define CPU_C1 ").Append(c1.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        source.Append("#define CPU_C2 ").Append(c2.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        source.Append(Read("ops")).Append(Read("kernels")).Append(Read("producer"));
        using var shaderc = Shaderc.GetApi();
        var compiler = shaderc.CompilerInitialize();
        var options = shaderc.CompileOptionsInitialize();
        try
        {
            if (compiler == null || options == null) throw new InvalidOperationException("Could not initialize shaderc.");
            // Preserve the explicit scalar operation order; ops.glsl also emits NoContraction.
            shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Zero);
            byte[] utf8 = Encoding.UTF8.GetBytes(source.ToString());
            fixed (byte* text = utf8)
            {
                var result = shaderc.CompileIntoSpv(compiler, text, (nuint)utf8.Length, ShaderKind.ComputeShader, "noise.comp", "main", options);
                if (result == null) throw new InvalidOperationException("Shader compiler returned no result.");
                try
                {
                    if (shaderc.ResultGetCompilationStatus(result) != CompilationStatus.Success)
                        throw new InvalidOperationException(Marshal.PtrToStringUTF8((nint)shaderc.ResultGetErrorMessage(result)));
                    return new ReadOnlySpan<byte>(shaderc.ResultGetBytes(result), checked((int)shaderc.ResultGetLength(result))).ToArray();
                }
                finally { shaderc.ResultRelease(result); }
            }
        }
        finally
        {
            if (options != null) shaderc.CompileOptionsRelease(options);
            if (compiler != null) shaderc.CompilerRelease(compiler);
        }
    }

    private static void AppendTable(StringBuilder source, string name, ReadOnlySpan<float> values)
    {
        source.Append("const float ").Append(name).Append("[] = float[](");
        for (int i = 0; i < values.Length; i++)
        {
            if (i != 0) source.Append(',');
            string literal = values[i].ToString("R", CultureInfo.InvariantCulture);
            source.Append(literal);
            if (!literal.Contains('.') && !literal.Contains('E')) source.Append(".0");
        }
        source.Append(");\n");
    }

    private static void Check(Result result)
    {
        if (result != Result.Success) throw new InvalidOperationException($"Vulkan: {result}.");
    }

    /// <summary>Destroys owned Vulkan resources. The caller must first complete all recorded work.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pipeline.Handle != 0) _vk.DestroyPipeline(_device, _pipeline, null);
        if (_layout.Handle != 0) _vk.DestroyPipelineLayout(_device, _layout, null);
        if (_pool.Handle != 0) _vk.DestroyDescriptorPool(_device, _pool, null);
        if (_setLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _setLayout, null);
    }

    /// <summary>A descriptor binding for a caller-owned output buffer. Disposing it does not free the buffer.</summary>
    public sealed class Output : IDisposable
    {
        internal VulkanNoiseProducer Owner { get; }
        internal Buffer Buffer { get; }
        internal DescriptorSet Set { get; }
        internal ulong Offset { get; }
        internal bool IsDisposed { get; private set; }
        /// <summary>Bound range length in bytes.</summary>
        public ulong CapacityBytes { get; }
        internal Output(VulkanNoiseProducer owner, Buffer buffer, DescriptorSet set, ulong offset, ulong capacity)
        { Owner = owner; Buffer = buffer; Set = set; Offset = offset; CapacityBytes = capacity; }
        /// <summary>Frees the descriptor once its recorded work has completed.</summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (!Owner._disposed) { var set = Set; Check(Owner._vk.FreeDescriptorSets(Owner._device, Owner._pool, 1, in set)); }
        }
    }
}
