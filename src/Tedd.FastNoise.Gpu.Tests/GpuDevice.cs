// Headless Vulkan test fixture. Opt-in tests must execute real compute work, never silently fall back.
// Coherent host-visible storage permits inspection after a fence without renderer or window dependencies.
using System;
using Silk.NET.Vulkan;
using Tedd.FastNoise.Gpu;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tedd.FastNoise.Gpu.Tests;

internal sealed unsafe class GpuDevice : IDisposable
{
    internal readonly Vk Vk = Vk.GetApi();
    private Instance _instance;
    private Device _device;
    private Queue _queue;
    private CommandPool _pool;
    private CommandBuffer _commands;
    private Buffer _buffer;
    private DeviceMemory _memory;
    private void* _mapped;
    internal VulkanNoiseProducer Producer { get; private set; } = null!;
    internal VulkanNoiseProducer.Output Output { get; private set; } = null!;
    internal string DeviceName { get; private set; } = "";
    // Descriptor starts inside the allocation to exercise nonzero output offsets.
    private ulong _offset;
    private const ulong Capacity = 4 * 1024 * 1024;

    internal GpuDevice()
    {
        try
        {
            var app = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version11 };
            var instanceInfo = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &app };
            Check(Vk.CreateInstance(in instanceInfo, null, out _instance));
            uint count = 0;
            Check(Vk.EnumeratePhysicalDevices(_instance, ref count, null));
            var devices = new PhysicalDevice[count];
            fixed (PhysicalDevice* list = devices) Check(Vk.EnumeratePhysicalDevices(_instance, ref count, list));
            PhysicalDevice physical = default;
            uint family = uint.MaxValue;
            foreach (var candidate in devices)
            {
                uint families = 0;
                Vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref families, null);
                var properties = new QueueFamilyProperties[families];
                fixed (QueueFamilyProperties* list = properties) Vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref families, list);
                for (uint i = 0; i < families; i++)
                    if ((properties[i].QueueFlags & QueueFlags.ComputeBit) != 0) { physical = candidate; family = i; break; }
                if (family != uint.MaxValue) break;
            }
            if (family == uint.MaxValue) throw new InvalidOperationException("No Vulkan compute device.");
            Vk.GetPhysicalDeviceProperties(physical, out var physicalProperties);
            DeviceName = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)physicalProperties.DeviceName)!;
            _offset = Math.Max(256ul, physicalProperties.Limits.MinStorageBufferOffsetAlignment);
            float priority = 1;
            var queueInfo = new DeviceQueueCreateInfo
            { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = family, QueueCount = 1, PQueuePriorities = &priority };
            var deviceInfo = new DeviceCreateInfo { SType = StructureType.DeviceCreateInfo, QueueCreateInfoCount = 1, PQueueCreateInfos = &queueInfo };
            Check(Vk.CreateDevice(physical, in deviceInfo, null, out _device));
            Vk.GetDeviceQueue(_device, family, 0, out _queue);
            var poolInfo = new CommandPoolCreateInfo
            { SType = StructureType.CommandPoolCreateInfo, Flags = CommandPoolCreateFlags.ResetCommandBufferBit, QueueFamilyIndex = family };
            Check(Vk.CreateCommandPool(_device, in poolInfo, null, out _pool));
            var allocate = new CommandBufferAllocateInfo
            { SType = StructureType.CommandBufferAllocateInfo, CommandPool = _pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
            Check(Vk.AllocateCommandBuffers(_device, in allocate, out _commands));
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo, Size = Capacity + _offset,
                Usage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, SharingMode = SharingMode.Exclusive
            };
            Check(Vk.CreateBuffer(_device, in bufferInfo, null, out _buffer));
            Vk.GetBufferMemoryRequirements(_device, _buffer, out var requirements);
            Vk.GetPhysicalDeviceMemoryProperties(physical, out var memory);
            uint type = uint.MaxValue;
            var flags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
            for (uint i = 0; i < memory.MemoryTypeCount; i++)
                if ((requirements.MemoryTypeBits & (1u << (int)i)) != 0 && (memory.MemoryTypes[(int)i].PropertyFlags & flags) == flags) { type = i; break; }
            if (type == uint.MaxValue) throw new InvalidOperationException("No coherent host-visible memory.");
            var memoryInfo = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = type };
            Check(Vk.AllocateMemory(_device, in memoryInfo, null, out _memory));
            Check(Vk.BindBufferMemory(_device, _buffer, _memory, 0));
            void* mapped;
            Check(Vk.MapMemory(_device, _memory, 0, requirements.Size, 0, &mapped));
            _mapped = mapped;
            new Span<byte>(_mapped, (int)(Capacity + _offset)).Fill(0xcd);
            Producer = new VulkanNoiseProducer(Vk, physical, _device);
            Output = Producer.BindOutput(_buffer, Capacity, _offset);
        }
        catch { Dispose(); throw; }
    }

    internal uint[] Run(Action<CommandBuffer> record, int wordCount)
    {
        Check(Vk.ResetCommandBuffer(_commands, 0));
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        Check(Vk.BeginCommandBuffer(_commands, in begin));
        record(_commands);
        var barrier = new MemoryBarrier
        { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.ShaderWriteBit | AccessFlags.TransferWriteBit, DstAccessMask = AccessFlags.HostReadBit };
        Vk.CmdPipelineBarrier(_commands, PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit, 0, 1, in barrier, 0, null, 0, null);
        Check(Vk.EndCommandBuffer(_commands));
        var commands = _commands;
        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &commands };
        Check(Vk.QueueSubmit(_queue, 1, in submit, default));
        Check(Vk.QueueWaitIdle(_queue));
        foreach (byte b in new ReadOnlySpan<byte>(_mapped, (int)_offset)) Assert.Equal(0xcd, b);
        return new ReadOnlySpan<uint>((byte*)_mapped + _offset, wordCount).ToArray();
    }

    private static void Check(Result result)
    { if (result != Result.Success) throw new InvalidOperationException(result.ToString()); }

    public void Dispose()
    {
        if (_device.Handle != 0) Vk.DeviceWaitIdle(_device);
        Output?.Dispose(); Producer?.Dispose();
        if (_mapped != null) Vk.UnmapMemory(_device, _memory);
        if (_buffer.Handle != 0) Vk.DestroyBuffer(_device, _buffer, null);
        if (_memory.Handle != 0) Vk.FreeMemory(_device, _memory, null);
        if (_pool.Handle != 0) Vk.DestroyCommandPool(_device, _pool, null);
        if (_device.Handle != 0) Vk.DestroyDevice(_device, null);
        if (_instance.Handle != 0) Vk.DestroyInstance(_instance, null);
        Vk.Dispose();
    }
}
