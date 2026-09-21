// Owns a graph-specialized compute pipeline on a caller-owned Vulkan device.
// Buffer allocation, submission and lifetime synchronization remain with the renderer;
// regular-grid dispatch constructs coordinates on-device without CPU input arrays.
using System;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Tedd.FastNoise.Procedural;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tedd.FastNoise.Gpu;

/// <summary>Records optimized graph evaluation directly into caller-owned GPU storage.</summary>
/// <remarks>The logical device must have shaderFloat64 enabled. Calls need external synchronization.
/// Keep producer, bindings and buffers alive until all submitted work completes. The caller owns
/// input visibility, queue ownership transfers and synchronization against earlier output users.</remarks>
public sealed unsafe class VulkanGraphProducer : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDeviceLimits _limits;
    private readonly int _inputCount, _outputCount;
    private DescriptorSetLayout _setLayout;
    private DescriptorPool _pool;
    private PipelineLayout _layout;
    private Pipeline _pipeline;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public double X, Y, Z, Step;
        public uint Width, Height, Depth, Mode;
        public uint Sky, UseSky, Reserved0, Reserved1;
    }

    /// <summary>Compiles a configured graph once and creates a reusable compute pipeline.</summary>
    /// <param name="vk">Vulkan API.</param>
    /// <param name="physicalDevice">Physical device underlying the logical device.</param>
    /// <param name="device">Caller-owned logical device with shaderFloat64 enabled.</param>
    /// <param name="graph">Immutable graph containing specialized constants and noise settings.</param>
    /// <param name="shaderFloat64Enabled">Explicit declaration of the enabled logical-device feature; physical support alone is insufficient.</param>
    /// <param name="maximumBindings">Maximum simultaneously allocated input/output bindings.</param>
    public VulkanGraphProducer(Vk vk, PhysicalDevice physicalDevice, Device device, CompiledProceduralGraph graph,
        bool shaderFloat64Enabled, uint maximumBindings = 1024)
        : this(vk, physicalDevice, device, graph?.InputCount ?? throw new ArgumentNullException(nameof(graph)),
            graph.Outputs.Count, () => VulkanGraphShaderCompiler.CompileCached(graph), shaderFloat64Enabled, maximumBindings) { }

    internal VulkanGraphProducer(Vk vk, PhysicalDevice physicalDevice, Device device, int inputCount, int outputCount,
        Func<byte[]> compile, bool shaderFloat64Enabled, uint maximumBindings)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentOutOfRangeException.ThrowIfZero(maximumBindings);
        vk.GetPhysicalDeviceFeatures(physicalDevice, out var features);
        if (!shaderFloat64Enabled || !features.ShaderFloat64)
            throw new NotSupportedException("Procedural graph GPU execution requires shaderFloat64 enabled on the logical Vulkan device.");
        _vk = vk; _device = device; _inputCount = inputCount; _outputCount = outputCount;
        vk.GetPhysicalDeviceProperties(physicalDevice, out var properties);
        _limits = properties.Limits;
        try
        {
            DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
            bindings[0] = new(0, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
            bindings[1] = new(1, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
            bindings[2] = new(2, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
            var setInfo = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 3, PBindings = bindings };
            Check(vk.CreateDescriptorSetLayout(device, in setInfo, null, out _setLayout));
            var poolSize = new DescriptorPoolSize(DescriptorType.StorageBuffer, checked(maximumBindings * 3));
            var poolInfo = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = maximumBindings,
                Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit, PoolSizeCount = 1, PPoolSizes = &poolSize };
            Check(vk.CreateDescriptorPool(device, in poolInfo, null, out _pool));
            var setLayout = _setLayout;
            var range = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Parameters));
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1,
                PSetLayouts = &setLayout, PushConstantRangeCount = 1, PPushConstantRanges = &range };
            Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out _layout));
            byte[] spirv = compile();
            fixed (byte* code = spirv)
            {
                var info = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)code };
                Check(vk.CreateShaderModule(device, in info, null, out var module));
                try
                {
                    byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
                    var stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.ComputeBit, Module = module, PName = entry };
                    var pipelineInfo = new ComputePipelineCreateInfo { SType = StructureType.ComputePipelineCreateInfo, Stage = stage, Layout = _layout };
                    Check(vk.CreateComputePipelines(device, default, 1, in pipelineInfo, null, out _pipeline));
                }
                finally { vk.DestroyShaderModule(device, module, null); }
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Required output bytes for interleaved double values in graph output order.</summary>
    public ulong RequiredOutputBytes(int sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        return checked((ulong)sampleCount * (ulong)_outputCount * sizeof(double));
    }

    /// <summary>Binds input/output storage ranges. Inputs are sample-major interleaved doubles.</summary>
    public Binding Bind(Buffer input, ulong inputBytes, Buffer output, ulong outputBytes, ulong inputOffset = 0, ulong outputOffset = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRange(input, inputOffset, inputBytes);
        ValidateRange(output, outputOffset, outputBytes);
        if (input.Handle == output.Handle && inputOffset < outputOffset + outputBytes && outputOffset < inputOffset + inputBytes)
            throw new ArgumentException("Input and output storage ranges must not overlap.");
        return CreateBinding(input, inputOffset, inputBytes, output, outputOffset, outputBytes, false);
    }

    /// <summary>Binds grid output only; the shader constructs its first three inputs as world X/Y/Z.</summary>
    public Binding BindGridOutput(Buffer output, ulong capacityBytes, ulong offsetBytes = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inputCount > 3) throw new InvalidOperationException("Grid generation requires a graph with at most three coordinate inputs.");
        ValidateRange(output, offsetBytes, capacityBytes);
        // The unused input binding may legally reference the output allocation; grid mode never reads it.
        return CreateBinding(output, offsetBytes, capacityBytes, output, offsetBytes, capacityBytes, true);
    }

    private Binding CreateBinding(Buffer input, ulong inputOffset, ulong inputBytes, Buffer output, ulong outputOffset, ulong outputBytes, bool grid,
        Buffer sky = default, ulong skyOffset = 0, ulong skyBytes = 0)
    {
        var layout = _setLayout;
        var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool, DescriptorSetCount = 1, PSetLayouts = &layout };
        Check(_vk.AllocateDescriptorSets(_device, in allocate, out var set));
        DescriptorBufferInfo* buffers = stackalloc DescriptorBufferInfo[3];
        buffers[0] = new(input, inputOffset, inputBytes); buffers[1] = new(output, outputOffset, outputBytes);
        buffers[2] = sky.Handle != 0 ? new(sky, skyOffset, skyBytes) : buffers[1];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[3];
        for (uint i = 0; i < 3; i++) writes[i] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet,
            DstSet = set, DstBinding = i, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = buffers + i };
        _vk.UpdateDescriptorSets(_device, 3, writes, 0, null);
        return new Binding(this, set, inputBytes, outputBytes, grid);
    }

    /// <summary>Records graph evaluation for existing GPU input samples.</summary>
    public void Record(CommandBuffer commands, Binding binding, int sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        ValidateBinding(binding, sampleCount);
        if (binding.Grid || binding.InputBytes < checked((ulong)sampleCount * (ulong)_inputCount * sizeof(double)))
            throw new ArgumentException("Binding has no suitable input sample range.", nameof(binding));
        if (sampleCount == 0) return;
        var p = new Parameters { Width = (uint)sampleCount, Height = 1, Depth = 1 };
        Dispatch(commands, binding, ref p, sampleCount);
    }

    /// <summary>Generates a regular grid directly on GPU, in X-fastest order, without input uploads.</summary>
    public void RecordGrid(CommandBuffer commands, Binding binding, double originX, double originY, double originZ,
        int width, int height, int depth, double step = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        if (!double.IsFinite(originX) || !double.IsFinite(originY) || !double.IsFinite(originZ) || !double.IsFinite(step) || step <= 0
            || !double.IsFinite(originX + (width - 1d) * step) || !double.IsFinite(originY + (height - 1d) * step)
            || !double.IsFinite(originZ + (depth - 1d) * step)) throw new ArgumentOutOfRangeException(nameof(step), "Grid coordinates must remain finite and step positive.");
        int count = checked(width * height * depth);
        ValidateBinding(binding, count);
        if (!binding.Grid) throw new ArgumentException("Use a grid output binding for direct generation.", nameof(binding));
        var p = new Parameters { X = originX, Y = originY, Z = originZ, Step = step, Width = (uint)width, Height = (uint)height, Depth = (uint)depth, Mode = 1 };
        Dispatch(commands, binding, ref p, count);
    }

    private void Dispatch(CommandBuffer commands, Binding binding, ref Parameters p, int count)
    {
        uint groups = ((uint)count + 63) / 64;
        DispatchGroups(commands, binding, ref p, groups, 1, 1);
    }

    internal Binding BindTerrain(Buffer input, ulong inputBytes, Buffer output, ulong outputBytes, Buffer sky, ulong skyBytes,
        ulong inputOffset, ulong outputOffset, ulong skyOffset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRange(input, inputOffset, inputBytes); ValidateRange(output, outputOffset, outputBytes);
        if (sky.Handle != 0) ValidateRange(sky, skyOffset, skyBytes);
        if (input.Handle == output.Handle && inputOffset < outputOffset + outputBytes && outputOffset < inputOffset + inputBytes)
            throw new ArgumentException("Column scratch and terrain output ranges must not overlap.");
        if (sky.Handle == output.Handle && skyOffset < outputOffset + outputBytes && outputOffset < skyOffset + skyBytes)
            throw new ArgumentException("Skylight and terrain output ranges must not overlap.");
        if (sky.Handle != 0 && sky.Handle == input.Handle && skyOffset < inputOffset + inputBytes && inputOffset < skyOffset + skyBytes)
            throw new ArgumentException("Skylight and writable column scratch ranges must not overlap.");
        return CreateBinding(input, inputOffset, inputBytes, output, outputOffset, outputBytes, false, sky, skyOffset, skyBytes);
    }

    internal void RecordTerrainKernel(CommandBuffer commands, Binding binding, double x, double y, double z, double step,
        uint side, uint mode, uint sky, uint skyMode, uint groups)
    {
        ValidateBinding(binding, 0);
        var p = new Parameters { X = x, Y = y, Z = z, Step = step, Width = side, Height = side, Depth = side,
            Mode = mode, Sky = sky, UseSky = skyMode };
        DispatchGroups(commands, binding, ref p, groups, groups, groups);
    }

    private void DispatchGroups(CommandBuffer commands, Binding binding, ref Parameters p, uint x, uint y, uint z)
    {
        if (x > _limits.MaxComputeWorkGroupCount[0] || y > _limits.MaxComputeWorkGroupCount[1] || z > _limits.MaxComputeWorkGroupCount[2])
            throw new ArgumentOutOfRangeException(nameof(x), "Dispatch exceeds device workgroup limits.");
        _vk.CmdBindPipeline(commands, PipelineBindPoint.Compute, _pipeline);
        var set = binding.Set;
        _vk.CmdBindDescriptorSets(commands, PipelineBindPoint.Compute, _layout, 0, 1, in set, 0, null);
        fixed (Parameters* data = &p) _vk.CmdPushConstants(commands, _layout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Parameters), data);
        _vk.CmdDispatch(commands, x, y, z);
        var barrier = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.TransferReadBit };
        _vk.CmdPipelineBarrier(commands, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.AllCommandsBit, 0, 1, in barrier, 0, null, 0, null);
    }

    private void ValidateRange(Buffer buffer, ulong offset, ulong bytes)
    {
        if (buffer.Handle == 0 || bytes == 0 || bytes % 8 != 0 || bytes > _limits.MaxStorageBufferRange
            || offset % Math.Max(8ul, _limits.MinStorageBufferOffsetAlignment) != 0 || offset > ulong.MaxValue - bytes)
            throw new ArgumentException("Invalid double storage-buffer range, capacity or alignment.");
    }

    private void ValidateBinding(Binding binding, int sampleCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(binding);
        ObjectDisposedException.ThrowIf(binding.IsDisposed, binding);
        if (binding.Owner != this || binding.OutputBytes < RequiredOutputBytes(sampleCount))
            throw new ArgumentException("Binding belongs to another graph producer or is too small.", nameof(binding));
    }

    private static void Check(Result result)
    { if (result != Result.Success) throw new InvalidOperationException($"Vulkan: {result}."); }

    /// <summary>Destroys owned resources after all work using them has completed.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pipeline.Handle != 0) _vk.DestroyPipeline(_device, _pipeline, null);
        if (_layout.Handle != 0) _vk.DestroyPipelineLayout(_device, _layout, null);
        if (_pool.Handle != 0) _vk.DestroyDescriptorPool(_device, _pool, null);
        if (_setLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _setLayout, null);
    }

    /// <summary>Descriptor ownership only; disposal never frees caller buffers.</summary>
    public sealed class Binding : IDisposable
    {
        internal VulkanGraphProducer Owner { get; }
        internal DescriptorSet Set { get; }
        internal ulong InputBytes { get; }
        internal ulong OutputBytes { get; }
        internal bool Grid { get; }
        internal bool IsDisposed { get; private set; }
        internal Binding(VulkanGraphProducer owner, DescriptorSet set, ulong inputBytes, ulong outputBytes, bool grid)
        { Owner = owner; Set = set; InputBytes = inputBytes; OutputBytes = outputBytes; Grid = grid; }
        /// <summary>Frees descriptors after their final recorded use completes.</summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (!Owner._disposed) { var set = Set; Check(Owner._vk.FreeDescriptorSets(Owner._device, Owner._pool, 1, in set)); }
        }
    }
}
