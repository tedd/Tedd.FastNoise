using System;
using System.Numerics;
using Tedd.FastNoise.Internal;
using Tedd.FastNoise.Internal.Kernels;

namespace Tedd.FastNoise;

public sealed partial class NoiseGenerator
{
    /// <summary>
    /// Sample count from which <see cref="NoiseBackend.Auto"/> starts using every core.
    /// </summary>
    /// <remarks>
    /// A fill has to be big enough to pay for waking the thread pool. The default is roughly a
    /// 128x128 tile, which is comfortably past break-even for every kernel here on typical
    /// hardware, and small enough that a 16x16x256 voxel column still goes parallel. Lower it if
    /// your fills are small but frequent and you have cores idle; raise it if you are already
    /// saturating the pool with chunk-level parallelism of your own, where nested parallelism just
    /// adds scheduling noise.
    /// </remarks>
    public int ParallelThreshold { get; set; } = 16384;

    /// <summary>Fills <paramref name="destination"/> with 2D noise over <paramref name="region"/>.</summary>
    /// <param name="destination">Receives <c>region.SampleCount</c> values, X-fastest. May be longer.</param>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy. <see cref="NoiseBackend.Auto"/> picks per call.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="region"/> has a non-positive dimension or step.</exception>
    public void Fill(Span<float> destination, in GridRegion2D region, NoiseBackend backend = NoiseBackend.Auto)
    {
        region.Validate(destination.Length);

        (int octaves, float fade) = Lod.Resolve(_frequency, _lacunarity, _octaves, region.Step);
        FillPlan plan = PlanFill(backend, region.SampleCount);

        if (plan.TryAccelerator is { } accelerator
            && accelerator.TryFill2D(destination, BuildRequest2D(region, octaves, fade)))
        {
            return;
        }

        KernelConfig kernel = BuildKernelConfig();
        FractalConfig fractal = BuildFractalConfig();
        int seed = _seed;
        float frequency = _frequency;
        GridRegion2D captured = region;

        if (_noiseType == NoiseType.OpenSimplex2S)
        {
            FastNoiseLiteCore core = ReferenceCore();
            RowScheduler.Run(destination, region.Height, plan.Parallel,
                (buffer, first, count) => GridFill.ReferenceRows2(core, captured, buffer, first, count));
            return;
        }

        if (plan.Wide)
        {
            RowScheduler.Run(destination, region.Height, plan.Parallel,
                (buffer, first, count) => GridFill.Rows2<VectorOps, Vector<float>, Vector<int>>(
                    kernel, fractal, seed, frequency, captured, octaves, fade, buffer, first, count));
        }
        else
        {
            RowScheduler.Run(destination, region.Height, plan.Parallel,
                (buffer, first, count) => GridFill.Rows2<ScalarOps, float, int>(
                    kernel, fractal, seed, frequency, captured, octaves, fade, buffer, first, count));
        }
    }

    /// <summary>Fills <paramref name="destination"/> with 3D noise over <paramref name="region"/>.</summary>
    /// <param name="destination">Receives <c>region.SampleCount</c> values, X-fastest then Y then Z. May be longer.</param>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy. <see cref="NoiseBackend.Auto"/> picks per call.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="region"/> has a non-positive dimension or step.</exception>
    public void Fill(Span<float> destination, in GridRegion3D region, NoiseBackend backend = NoiseBackend.Auto)
    {
        region.Validate(destination.Length);

        (int octaves, float fade) = Lod.Resolve(_frequency, _lacunarity, _octaves, region.Step);
        FillPlan plan = PlanFill(backend, region.SampleCount);

        if (plan.TryAccelerator is { } accelerator
            && accelerator.TryFill3D(destination, BuildRequest3D(region, octaves, fade)))
        {
            return;
        }

        KernelConfig kernel = BuildKernelConfig();
        FractalConfig fractal = BuildFractalConfig();
        int seed = _seed;
        float frequency = _frequency;
        GridRegion3D captured = region;
        int totalRows = region.Height * region.Depth;

        if (_noiseType == NoiseType.OpenSimplex2S)
        {
            FastNoiseLiteCore core = ReferenceCore();
            RowScheduler.Run(destination, totalRows, plan.Parallel,
                (buffer, first, count) => GridFill.ReferenceRows3(core, captured, buffer, first, count));
            return;
        }

        if (plan.Wide)
        {
            RowScheduler.Run(destination, totalRows, plan.Parallel,
                (buffer, first, count) => GridFill.Rows3<VectorOps, Vector<float>, Vector<int>>(
                    kernel, fractal, seed, frequency, captured, octaves, fade, buffer, first, count));
        }
        else
        {
            RowScheduler.Run(destination, totalRows, plan.Parallel,
                (buffer, first, count) => GridFill.Rows3<ScalarOps, float, int>(
                    kernel, fractal, seed, frequency, captured, octaves, fade, buffer, first, count));
        }
    }

    /// <summary>Allocates an array and fills it with 2D noise over <paramref name="region"/>.</summary>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy.</param>
    /// <returns>A new array of <c>region.SampleCount</c> values.</returns>
    public float[] Create(in GridRegion2D region, NoiseBackend backend = NoiseBackend.Auto)
    {
        float[] result = new float[region.SampleCount];
        Fill(result, region, backend);
        return result;
    }

    /// <summary>Allocates an array and fills it with 3D noise over <paramref name="region"/>.</summary>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy.</param>
    /// <returns>A new array of <c>region.SampleCount</c> values.</returns>
    public float[] Create(in GridRegion3D region, NoiseBackend backend = NoiseBackend.Auto)
    {
        float[] result = new float[region.SampleCount];
        Fill(result, region, backend);
        return result;
    }

    /// <summary>How one fill will actually be executed, after every fallback has been applied.</summary>
    private readonly record struct FillPlan(bool Wide, bool Parallel, INoiseAccelerator? TryAccelerator);

    /// <summary>Turns a requested backend into a plan the hardware and the noise type can honour.</summary>
    private FillPlan PlanFill(NoiseBackend requested, int sampleCount)
    {
        INoiseAccelerator? accelerator = requested == NoiseBackend.Gpu ? NoiseAccelerator.For(sampleCount) : null;

        // OpenSimplex2S has no wide kernel; nothing goes wide without hardware vectors either.
        bool canGoWide = Vector.IsHardwareAccelerated
            && Vector<float>.Count > 1
            && NoisePipeline.HasWideKernel(_noiseType);

        bool wide = requested != NoiseBackend.Scalar && canGoWide;

        bool parallel = requested switch
        {
            NoiseBackend.Scalar or NoiseBackend.Simd => false,
            NoiseBackend.Parallel => sampleCount >= ParallelThreshold,

            // Auto and a declined GPU both land on the same size heuristic.
            _ => sampleCount >= ParallelThreshold,
        };

        return new FillPlan(wide, parallel, accelerator);
    }

    /// <summary>Flattens this generator plus a region into a self-contained accelerator request.</summary>
    private NoiseFillRequest2D BuildRequest2D(in GridRegion2D region, int octaves, float lastOctaveFade) => new()
    {
        Seed = _seed,
        Frequency = _frequency,
        NoiseType = _noiseType,
        FractalType = _fractalType,
        Octaves = octaves,
        Lacunarity = _lacunarity,
        Gain = _gain,
        WeightedStrength = _weightedStrength,
        PingPongStrength = _pingPongStrength,
        FractalBounding = _fractalBounding,
        CellularDistance = _cellularDistanceFunction,
        CellularReturn = _cellularReturnType,
        CellularJitter = _cellularJitter,
        LastOctaveFade = lastOctaveFade,
        Region = region,
    };

    /// <summary>Flattens this generator plus a region into a self-contained accelerator request.</summary>
    private NoiseFillRequest3D BuildRequest3D(in GridRegion3D region, int octaves, float lastOctaveFade) => new()
    {
        Seed = _seed,
        Frequency = _frequency,
        NoiseType = _noiseType,
        RotationType3D = _rotationType3D,
        FractalType = _fractalType,
        Octaves = octaves,
        Lacunarity = _lacunarity,
        Gain = _gain,
        WeightedStrength = _weightedStrength,
        PingPongStrength = _pingPongStrength,
        FractalBounding = _fractalBounding,
        CellularDistance = _cellularDistanceFunction,
        CellularReturn = _cellularReturnType,
        CellularJitter = _cellularJitter,
        LastOctaveFade = lastOctaveFade,
        Region = region,
    };
}
