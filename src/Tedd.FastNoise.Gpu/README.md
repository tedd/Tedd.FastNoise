# Tedd.FastNoise.Gpu

Vulkan compute generation into caller-owned GPU buffers. Targets .NET 10 and uses Silk.NET Vulkan
and shaderc. The CPU library has no GPU dependency. Install the optional GPU package:

```powershell
dotnet add package Tedd.FastNoise.Gpu
```

`RecordNoise` produces 2D or 3D float32 fields. `RecordTerrain` evaluates a 3D density field,
classifies voxels and compacts 4³ bricks into Forcecraft10's production voxel payload. Generation
and rendering can use the same buffer without CPU readback.

For complete device setup and executable examples, run the
[Vulkan sample](https://github.com/tedd/Tedd.FastNoise/tree/main/samples/Tedd.FastNoise.Gpu.Sample):

```powershell
dotnet run -c Release --project samples/Tedd.FastNoise.Gpu.Sample -- artifacts/gpu-sample
```

The sample saves a float-field image, terrain previews at three LODs, and packed binary payloads.

## Record terrain

```csharp
using Tedd.FastNoise;
using Tedd.FastNoise.Gpu;

// Reuse the renderer's Vk, PhysicalDevice and Device. Retain the producer across chunks.
using var producer = new VulkanNoiseProducer(vk, physicalDevice, device);
var noise = new NoiseGenerator(1337)
{
    NoiseType = NoiseType.OpenSimplex2,
    Frequency = 0.003f,
    FractalType = FractalType.FBm,
    Octaves = 6,
    Lod = LodPolicy.Automatic,
};

int side = 32;
float spacing = 8; // world units per voxel, ordinarily 2^LOD
var region = new GridRegion3D(worldX, worldY, worldZ, side, side, side, spacing);
var request = noise.CreateRequest(region);
ulong capacity = VoxelTerrainSettings.RequiredBytes(side);

// Allocate at least capacity bytes with STORAGE_BUFFER | TRANSFER_DST usage.
// Forcecraft's buffer-reference rendering also requires SHADER_DEVICE_ADDRESS and
// the renderer's corresponding device-address allocation flags.
using var output = producer.BindOutput(buffer, capacity);
producer.RecordTerrain(commandBuffer, output, request,
    new VoxelTerrainSettings(Height: 64, Amplitude: 96));

// Submit on the renderer's compute-capable queue. Retain output, buffer and producer
// until the submission and all subsequent rendering using them have completed.
```

Density is `noise * Amplitude + Height - worldY * VerticalScale`; positive density produces a
full, opaque cell. Set `VerticalScale` to zero for thresholded 3D noise. This is a generic terrain
rule; it does not reproduce Forcecraft's Earth generator, biome materials, fluids or lighting.
Palette entry zero is air and entry one has six identical flat-color faces. Sky light is constant.

Regions must be cubic, with a power-of-two side from 8 through 256. Device storage-buffer limits
may restrict the maximum side. `Step` controls sample spacing and octave culling. Use an explicit
world origin when a LOD node covers multiple base chunks. `GridRegion3D.Chunk` instead interprets
chunk indices on a grid whose chunk width is `chunkSize * step`.

## Compiled procedural graphs

`VulkanGraphProducer` specializes a `CompiledProceduralGraph` into GLSL and optimized SPIR-V at
construction. `BindGridOutput` with `RecordGrid` generates interleaved double output records from
world coordinates; `Bind` with `Record` evaluates records already resident in GPU buffers.
Reuse producers and bindings, and retain all resources until submitted work completes.

`VulkanGraphTerrainProducer` accepts a column graph, a voxel graph, a planetary axis/sign/radius
and six four-word palette faces per material. The column graph receives datum X/Y/Z. The voxel
graph receives the column outputs followed by face-local elevation, and returns a material index.
Index zero is empty. Column scratch, classification and packed bricks remain on GPU. `Bind`
optionally accepts packed skylight bytes in Z-fastest order; without them `Record` supplies uniform
sky. With `skyIncludesHalo: true`, supply `(side + 2)^3` raw skylight bytes for coordinates `-1..side`;
the shader derives occupied-cell exposure as the maximum of the centre and six axial neighbours.
Otherwise supply `side^3` already-derived exposure bytes. `Record` takes the first sample's world coordinates: callers implementing centre-sampled LOD
must include their half-voxel offset. `RequiredBytes` and `RequiredScratchBytes` specify capacities.

The graph producers require `shaderFloat64` to be enabled when creating the logical device;
unsupported devices must use an application-selected fallback. Float64 power is unsupported.
Noise kernels support the algorithms listed below; noise lattice coordinates must fit signed
32-bit indices at every octave. Arithmetic order is preserved, but cross-device bit equivalence
is not guaranteed. Applications using threshold-sensitive terrain should validate their graph
and target devices against their authoritative CPU generator.

## Float fields

```csharp
var request = noise.CreateRequest(new GridRegion3D(0, 0, 0, 64, 64, 64));
// StorageBuffer usage; capacity >= request.Region.SampleCount * sizeof(float).
producer.RecordNoise(commandBuffer, output, request);
```

Float fields are X-fastest: `x + width * (y + height * z)`. 2D requests use the same overload name.
The shader supports OpenSimplex2, Perlin and Value, all three 3D rotation settings, and None,
FBm, Ridged and PingPong fractals with 1–128 octaves. Unsupported requests throw before recording.
`Supports` checks algorithm/fractal support; dispatch also validates dimensions, parameters and
capacity. Coordinates must remain within the signed 32-bit noise lattice at every octave;
requests outside the conservatively checked range are rejected. Compiled stacks and domain warp
require a separate implementation.

`CreateRequest` snapshots generator settings and resolves LOD on the CPU. Noise evaluation,
material classification and brick compaction execute on the GPU. GPU arithmetic preserves the
CPU operation order and disables multiply-add contraction, but cross-device bit equivalence is
not a public guarantee. The explicit producer does not register as `INoiseAccelerator`, whose
contract requires exact CPU equivalence. `NoiseBackend.Gpu` retains its existing fallback behavior.

## Forcecraft buffer contract

The output matches `VoxelVolumeData.Words` and `voxel_trace.frag`'s production brick addressing:

| Location | Meaning |
| --- | --- |
| Word 0 | Palette offset, in uint32 words from the bound range start |
| Words 1–3 | Zero: no temperature, sloped-surface or emission stream |
| Word `4 + (bx * brickSide + by) * brickSide + bz` | Brick directory entry |
| Directory entry 0 | Empty brick |
| Entry bit 31 | Uniform brick: one cell rather than 64 |
| Entry bits 0–30 | Absolute cell payload word offset |
| Cell | Shape and light, two uint32 words; Z-fastest within each brick |
| Palette | Two entries × six faces × four uint32 words; color, tileX, tileY, flags |

Solid cells have shape `0xff000001` and light `0xff000000 | (Sky << 16)`.
The used payload length is `(word[0] + 48) * 4` bytes. Brick offsets depend on workgroup execution
order, so raw payload bytes are not stable; compare decoded cells. Worst-case capacity remains
reserved even when the used payload is small. Empty bricks consume no cell words; uniform bricks
consume two words. No allocation counter or scratch data lies outside the renderer's payload.

Forcecraft integration must provide a GPU-buffer volume handle, its device address, extent,
world origin, voxel spacing and acceleration-structure bounds. Its current `VoxelVolumeData`
upload API accepts CPU data, so format compatibility alone does not connect this producer to the
chunk lifecycle. The host must select generation only for chunks known to be unmodified, key
caches by generator version/seed/region/LOD, and discard stale GPU results when an edit arrives.
Collision and authoritative gameplay still require their own CPU/world data. LOD filtering
smooths noise; it does not enforce identical occupancy across LOD levels or construct transitions.

## Synchronization and ownership

All producer and output calls require external synchronization. Reuse descriptor bindings for
the lifetime of each output buffer. The constructor's `maximumOutputs` bounds live bindings.
Buffers require valid bound memory and the declared capacity; the producer cannot inspect the
allocation behind a Vulkan handle. Buffer offsets must meet device storage-buffer alignment.

Record outside a render pass on a compute-capable queue. The producer inserts transfer-to-compute,
compaction-to-palette, and compute-to-shader/transfer-read barriers. The caller must synchronize
previous use before overwriting a buffer, perform cross-queue semaphore/ownership transfers,
and wait for completion before freeing resources. Host readback additionally requires a host-read
barrier and noncoherent-memory invalidation when applicable. The producer never submits or waits.

## Verification

```powershell
$env:FASTNOISE_GPU_TESTS = '1'
dotnet test src/Tedd.FastNoise.Gpu.Tests -c Release
```

Opt-in tests execute Vulkan compute and compare float bits with the scalar path, then decode
empty, uniform and mixed brick payloads at several LODs. Without the environment variable, GPU
tests are reported as skipped; CPU contract tests still run. An enabled test fails if Vulkan is
unavailable, rather than passing through a fallback.
