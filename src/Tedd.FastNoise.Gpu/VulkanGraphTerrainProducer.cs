// Executes a shared-column graph followed by voxel classification and renderer brick packing.
// Column intermediates stay device-resident; only optional propagated skylight and immutable palette
// data cross from the host, preserving the renderer's directory/cell/palette addressing contract.
using System;
using Silk.NET.Vulkan;
using Tedd.FastNoise.Procedural;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tedd.FastNoise.Gpu;

/// <summary>Generates material terrain through two specialized graph kernels without host voxel arrays.</summary>
/// <remarks>Column graph inputs are datum-plane world X/Y/Z; voxel graph inputs are the ordered column
/// outputs followed by signed height above the datum. The voxel output is a material palette index, with
/// zero meaning air; every nonzero output must be an integer less than the palette material count.
/// Palette entries comprise six faces of four uint words each. Full cube fill is assumed;
/// emission, thermal and sloped shape streams are not generated. The caller retains buffers and bindings
/// through GPU completion and supplies queue synchronization before reusing them.</remarks>
public sealed unsafe class VulkanGraphTerrainProducer : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanGraphProducer _columns;
    private readonly VulkanGraphProducer _voxels;
    private readonly int _verticalAxis, _upSign, _columnOutputs, _paletteWords;
    private readonly double _seaLevelRadius;
    private bool _disposed;

    /// <summary>Compiles configured column and voxel graphs once for one cube face and LOD.</summary>
    public VulkanGraphTerrainProducer(Vk vk, PhysicalDevice physicalDevice, Device device,
        CompiledProceduralGraph columnGraph, CompiledProceduralGraph voxelGraph, int verticalAxis, int upSign,
        double seaLevelRadius, uint[] paletteFaces, bool shaderFloat64Enabled, uint maximumBindings = 1024)
    {
        ArgumentNullException.ThrowIfNull(vk); ArgumentNullException.ThrowIfNull(columnGraph);
        ArgumentNullException.ThrowIfNull(voxelGraph); ArgumentNullException.ThrowIfNull(paletteFaces);
        if (verticalAxis is < 0 or > 2 || upSign is not (-1 or 1) || !double.IsFinite(seaLevelRadius))
            throw new ArgumentOutOfRangeException(nameof(verticalAxis), "A valid cube-face axis, sign and finite datum radius are required.");
        if (columnGraph.InputCount > 3 || voxelGraph.InputCount > columnGraph.Outputs.Count + 1 || voxelGraph.Outputs.Count != 1)
            throw new ArgumentException("Column graph takes world XYZ; voxel graph takes column outputs then elevation and returns one material.");
        if (paletteFaces.Length < 24 || paletteFaces.Length % 24 != 0 || paletteFaces.Length / 24 > 65536)
            throw new ArgumentException("Palette requires six four-word faces per material, including air.", nameof(paletteFaces));
        _vk = vk; _verticalAxis = verticalAxis; _upSign = upSign; _seaLevelRadius = seaLevelRadius;
        _columnOutputs = columnGraph.Outputs.Count; _paletteWords = paletteFaces.Length;
        _columns = new VulkanGraphProducer(vk, physicalDevice, device, columnGraph, shaderFloat64Enabled, maximumBindings);
        try
        {
            string source = VulkanGraphShaderCompiler.GenerateTerrainSource(voxelGraph, _columnOutputs, verticalAxis, upSign, seaLevelRadius, paletteFaces);
            _voxels = new VulkanGraphProducer(vk, physicalDevice, device, voxelGraph.InputCount, 0,
                () => VulkanNoiseProducer.CompileSource(source, optimize: true), shaderFloat64Enabled, maximumBindings);
        }
        catch { _columns.Dispose(); throw; }
    }

    /// <summary>Worst-case directory, uncompressed cells and palette bytes for a supported cubic side.</summary>
    public ulong RequiredBytes(int side)
    {
        ValidateSide(side);
        ulong bricks = (ulong)side / 4;
        return checked((4 + bricks * bricks * bricks + 2 * (ulong)side * (ulong)side * (ulong)side + (ulong)_paletteWords) * 4);
    }

    /// <summary>GPU-resident column intermediate bytes, reused across all vertical voxels.</summary>
    public ulong RequiredScratchBytes(int side)
    { ValidateSide(side); return checked((ulong)side * (ulong)side * (ulong)_columnOutputs * 8); }

    /// <summary>Binds caller-owned scratch, packed output and optional four-bytes-per-word Z-fastest skylight.</summary>
    /// <remarks>With skyIncludesHalo, sky contains a (side+2)^3 raw light field including one neighboring
    /// sample on every face. The shader computes the maximum of each occupied cell and its six axial
    /// neighbors, preserving surface exposure without preparing seven samples per voxel on the CPU.</remarks>
    public Binding Bind(Buffer scratch, ulong scratchBytes, Buffer output, ulong outputBytes,
        Buffer sky = default, ulong skyBytes = 0, ulong scratchOffset = 0, ulong outputOffset = 0, ulong skyOffset = 0,
        bool skyIncludesHalo = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var voxelBinding = _voxels.BindTerrain(scratch, scratchBytes, output, outputBytes, sky, skyBytes, scratchOffset, outputOffset, skyOffset);
        try
        {
            var columnBinding = _columns.BindGridOutput(scratch, scratchBytes, scratchOffset);
            return new Binding(this, columnBinding, voxelBinding, output, outputOffset, scratchBytes, outputBytes, sky.Handle != 0, skyBytes, skyIncludesHalo);
        }
        catch { voxelBinding.Dispose(); throw; }
    }

    /// <summary>Records column generation, material packing and palette publication on one compute queue.</summary>
    /// <param name="commands">Recording command buffer.</param>
    /// <param name="binding">Buffers retained until execution completes.</param>
    /// <param name="originX">World X of the first voxel sample (include half-voxel offset if desired).</param>
    /// <param name="originY">World Y of the first voxel sample.</param>
    /// <param name="originZ">World Z of the first voxel sample.</param>
    /// <param name="side">Power-of-two voxel count per axis, from 8 through 256.</param>
    /// <param name="step">World units per voxel.</param>
    /// <param name="sky">Uniform sky value used only when no skylight buffer is bound.</param>
    public void Record(CommandBuffer commands, Binding binding, double originX, double originY, double originZ,
        int side, double step = 1, uint sky = 255)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(binding);
        ObjectDisposedException.ThrowIf(binding.IsDisposed, binding);
        ValidateSide(side);
        ulong skySide = (ulong)(side + (binding.SkyIncludesHalo ? 2 : 0));
        if (binding.Owner != this || binding.ScratchBytes < RequiredScratchBytes(side) || binding.OutputBytes < RequiredBytes(side)
            || (binding.HasSky && binding.SkyBytes < skySide * skySide * skySide))
            throw new ArgumentException("Terrain binding belongs to another producer or has insufficient capacity.", nameof(binding));
        if (sky > 255) throw new ArgumentOutOfRangeException(nameof(sky));
        if (!double.IsFinite(originX) || !double.IsFinite(originY) || !double.IsFinite(originZ) || !double.IsFinite(step) || step <= 0
            || !double.IsFinite(originX + (side - 1d) * step) || !double.IsFinite(originY + (side - 1d) * step)
            || !double.IsFinite(originZ + (side - 1d) * step)) throw new ArgumentOutOfRangeException(nameof(step));
        double datum = _upSign * _seaLevelRadius;
        _columns.RecordGrid(commands, binding.Columns, _verticalAxis == 0 ? datum : originX,
            _verticalAxis == 1 ? datum : originY, _verticalAxis == 2 ? datum : originZ,
            _verticalAxis == 0 ? 1 : side, _verticalAxis == 1 ? 1 : side, _verticalAxis == 2 ? 1 : side, step);
        uint bricks = (uint)side / 4, headerWords = 4 + bricks * bricks * bricks;
        uint* header = stackalloc uint[] { headerWords, 0, 0, 0 };
        _vk.CmdUpdateBuffer(commands, binding.Output, binding.OutputOffset, 16, header);
        var barrier = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.TransferWriteBit | AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit };
        _vk.CmdPipelineBarrier(commands, PipelineStageFlags.TransferBit | PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit,
            0, 1, in barrier, 0, null, 0, null);
        uint skyMode = !binding.HasSky ? 0u : binding.SkyIncludesHalo ? 2u : 1u;
        _voxels.RecordTerrainKernel(commands, binding.Voxels, originX, originY, originZ, step, (uint)side, 2, sky, skyMode, bricks);
        _voxels.RecordTerrainKernel(commands, binding.Voxels, originX, originY, originZ, step, (uint)side, 3, sky, skyMode, 1);
        // Header words untouched by compute also need transfer-write visibility to the renderer.
        barrier.SrcAccessMask = AccessFlags.TransferWriteBit | AccessFlags.ShaderWriteBit;
        barrier.DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.TransferReadBit;
        _vk.CmdPipelineBarrier(commands, PipelineStageFlags.TransferBit | PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.AllCommandsBit,
            0, 1, in barrier, 0, null, 0, null);
    }

    private static void ValidateSide(int side)
    {
        if (side is < 8 or > 256 || (side & (side - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(side), "Side must be a power of two from 8 through 256.");
    }

    /// <summary>Destroys pipelines after their final submitted use completes.</summary>
    public void Dispose()
    { if (_disposed) return; _disposed = true; _voxels.Dispose(); _columns.Dispose(); }

    /// <summary>Owns only descriptor bindings; caller-owned storage survives disposal.</summary>
    public sealed class Binding : IDisposable
    {
        internal VulkanGraphTerrainProducer Owner { get; }
        internal VulkanGraphProducer.Binding Columns { get; }
        internal VulkanGraphProducer.Binding Voxels { get; }
        internal Buffer Output { get; }
        internal ulong OutputOffset { get; }
        internal ulong ScratchBytes { get; }
        internal ulong OutputBytes { get; }
        internal bool HasSky { get; }
        internal ulong SkyBytes { get; }
        internal bool SkyIncludesHalo { get; }
        internal bool IsDisposed { get; private set; }
        internal Binding(VulkanGraphTerrainProducer owner, VulkanGraphProducer.Binding columns, VulkanGraphProducer.Binding voxels,
            Buffer output, ulong offset, ulong scratchBytes, ulong outputBytes, bool hasSky, ulong skyBytes, bool skyIncludesHalo)
        { Owner = owner; Columns = columns; Voxels = voxels; Output = output; OutputOffset = offset; ScratchBytes = scratchBytes; OutputBytes = outputBytes; HasSky = hasSky; SkyBytes = skyBytes; SkyIncludesHalo = skyIncludesHalo; }
        /// <summary>Frees descriptor bindings once no recorded work references them.</summary>
        public void Dispose()
        { if (IsDisposed) return; IsDisposed = true; Voxels.Dispose(); Columns.Dispose(); }
    }
}
