using System;
using System.Collections.Generic;
using System.Numerics;
using Tedd.FastNoise.Internal;
using Tedd.FastNoise.Internal.Kernels;

namespace Tedd.FastNoise;

/// <summary>
/// An immutable, fused executor for a <see cref="NoiseStack"/>.
/// </summary>
/// <remarks>
/// <para>
/// Produced by <see cref="NoiseStack.Compile"/>. Safe to share across threads and to keep for the
/// lifetime of a world -- it holds no reference to the builder or to the generators it was built
/// from, so nothing can change under it.
/// </para>
/// <para>
/// A fill runs every layer against coordinates held in vector registers and writes the blended
/// result once. See <c>StackFill</c> for why that matters.
/// </para>
/// </remarks>
public sealed class CompiledNoiseStack
{
    private readonly LayerPlan[] _plans;
    private readonly LodPolicy _lod;
    private readonly bool _allLayersHaveWideKernels;

    /// <summary>Cache for the common case: the same step used over and over, so resolve level of detail once.</summary>
    private readonly Dictionary<float, ResolvedLayer[]> _resolvedByStep = [];

    /// <summary>Builds a compiled stack. Called by <see cref="NoiseStack.Compile"/>.</summary>
    internal CompiledNoiseStack(NoiseLayer[] layers, LodPolicy lod, int parallelThreshold)
    {
        _lod = lod;
        ParallelThreshold = parallelThreshold;
        _plans = new LayerPlan[layers.Length];
        _allLayersHaveWideKernels = true;

        for (int index = 0; index < layers.Length; index++)
        {
            NoiseLayer layer = layers[index];
            NoiseGenerator source = layer.Source;

            _plans[index] = new LayerPlan
            {
                Kernel = source.BuildKernelConfig(),
                Fractal = source.BuildFractalConfig(),
                Seed = source.Seed,
                Frequency = source.Frequency,
                Lacunarity = source.Lacunarity,
                Octaves = source.Octaves,
                Blend = layer.Blend,
                Amplitude = layer.Amplitude,
                Offset = layer.Offset,
                BlendFactor = layer.BlendFactor,
                FeatureSize = layer.FeatureSize,
                Name = layer.Name,
            };

            if (!NoisePipeline.HasWideKernel(source.NoiseType))
            {
                _allLayersHaveWideKernels = false;
            }
        }
    }

    /// <summary>Number of layers in the compiled stack, before any level-of-detail culling.</summary>
    public int LayerCount => _plans.Length;

    /// <summary>Sample count from which fills use every core.</summary>
    public int ParallelThreshold { get; }

    /// <summary>
    /// Whether fills of this stack can use vector registers.
    /// </summary>
    /// <remarks>
    /// False when any layer uses <see cref="NoiseType.OpenSimplex2S"/>, which has no wide kernel.
    /// A stack is fused as a unit, so one scalar-only layer holds the rest back; split it into a
    /// separate stack if that matters. Parallel execution is unaffected.
    /// </remarks>
    public bool IsVectorised => _allLayersHaveWideKernels && Vector.IsHardwareAccelerated && Vector<float>.Count > 1;

    /// <summary>Names the layers a fill at this sample spacing would actually evaluate.</summary>
    /// <param name="step">World units between samples.</param>
    /// <returns>The surviving layers' names, or their noise type where unnamed, in evaluation order.</returns>
    /// <remarks>
    /// For checking that a level-of-detail configuration does what was intended, rather than
    /// discovering from a profiler that the surface layers never got culled.
    /// </remarks>
    public IReadOnlyList<string> DescribeActiveLayers(float step)
    {
        List<string> names = [];

        foreach (LayerPlan plan in _plans)
        {
            if (!_lod.ShouldEvaluateLayer(plan.FeatureSize, step))
            {
                continue;
            }

            (int octaves, float fade) = _lod.Resolve(plan.Frequency, plan.Lacunarity, plan.Octaves, step);
            string name = plan.Name ?? plan.Kernel.NoiseType.ToString();

            // The fade matters as much as the count: one octave at 4% amplitude and one octave at
            // full amplitude cost the same and look nothing like each other.
            string detail = fade switch
            {
                <= 0f => "silent, below the sample grid",
                < 1f => $"{octaves}/{plan.Octaves} octaves, finest at {fade:P0}",
                _ => $"{octaves}/{plan.Octaves} octaves",
            };

            names.Add($"{name} ({detail})");
        }

        return names;
    }

    /// <summary>Samples the whole stack at one 2D point, at full detail.</summary>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    public float GetNoise(float x, float y)
        => StackFill.Evaluate2<ScalarOps, float, int>(Resolve(1f), x, y);

    /// <summary>Samples the whole stack at one 3D point, at full detail.</summary>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    /// <param name="z">World Z.</param>
    public float GetNoise(float x, float y, float z)
        => StackFill.Evaluate3<ScalarOps, float, int>(Resolve(1f), x, y, z);

    /// <summary>Fills <paramref name="destination"/> with the whole stack over <paramref name="region"/>.</summary>
    /// <param name="destination">Receives <c>region.SampleCount</c> values, X-fastest.</param>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public void Fill(Span<float> destination, in GridRegion2D region, NoiseBackend backend = NoiseBackend.Auto)
    {
        region.Validate(destination.Length);

        ResolvedLayer[] layers = Resolve(region.Step);
        if (layers.Length == 0)
        {
            destination[..region.SampleCount].Clear();
            return;
        }

        bool wide = backend != NoiseBackend.Scalar && IsVectorised;
        bool parallel = UseParallel(backend, region.SampleCount);
        GridRegion2D captured = region;

        if (wide)
        {
            RowScheduler.Run(destination, region.Height, parallel,
                (buffer, first, count) => StackFill.Rows2<VectorOps, Vector<float>, Vector<int>>(
                    layers, captured, buffer, first, count));
        }
        else
        {
            RowScheduler.Run(destination, region.Height, parallel,
                (buffer, first, count) => StackFill.Rows2<ScalarOps, float, int>(
                    layers, captured, buffer, first, count));
        }
    }

    /// <summary>Fills <paramref name="destination"/> with the whole stack over <paramref name="region"/>.</summary>
    /// <param name="destination">Receives <c>region.SampleCount</c> values, X-fastest then Y then Z.</param>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public void Fill(Span<float> destination, in GridRegion3D region, NoiseBackend backend = NoiseBackend.Auto)
    {
        region.Validate(destination.Length);

        ResolvedLayer[] layers = Resolve(region.Step);
        if (layers.Length == 0)
        {
            destination[..region.SampleCount].Clear();
            return;
        }

        bool wide = backend != NoiseBackend.Scalar && IsVectorised;
        bool parallel = UseParallel(backend, region.SampleCount);
        GridRegion3D captured = region;
        int totalRows = region.Height * region.Depth;

        if (wide)
        {
            RowScheduler.Run(destination, totalRows, parallel,
                (buffer, first, count) => StackFill.Rows3<VectorOps, Vector<float>, Vector<int>>(
                    layers, captured, buffer, first, count));
        }
        else
        {
            RowScheduler.Run(destination, totalRows, parallel,
                (buffer, first, count) => StackFill.Rows3<ScalarOps, float, int>(
                    layers, captured, buffer, first, count));
        }
    }

    /// <summary>Allocates an array and fills it with the whole stack.</summary>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy.</param>
    public float[] Create(in GridRegion2D region, NoiseBackend backend = NoiseBackend.Auto)
    {
        float[] result = new float[region.SampleCount];
        Fill(result, region, backend);
        return result;
    }

    /// <summary>Allocates an array and fills it with the whole stack.</summary>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy.</param>
    public float[] Create(in GridRegion3D region, NoiseBackend backend = NoiseBackend.Auto)
    {
        float[] result = new float[region.SampleCount];
        Fill(result, region, backend);
        return result;
    }

    private bool UseParallel(NoiseBackend backend, int sampleCount) => backend switch
    {
        NoiseBackend.Scalar or NoiseBackend.Simd => false,
        _ => sampleCount >= ParallelThreshold,
    };

    /// <summary>
    /// Applies the level-of-detail policy for a given sample spacing, and caches the result.
    /// </summary>
    /// <remarks>
    /// Worlds sample at a handful of distinct steps -- one per detail ring -- so this dictionary
    /// stays tiny and hits almost always. It is locked rather than concurrent because it is touched
    /// once per fill, not once per sample.
    /// </remarks>
    private ResolvedLayer[] Resolve(float step)
    {
        lock (_resolvedByStep)
        {
            if (_resolvedByStep.TryGetValue(step, out ResolvedLayer[]? cached))
            {
                return cached;
            }

            List<ResolvedLayer> resolved = [];

            foreach (LayerPlan plan in _plans)
            {
                if (!_lod.ShouldEvaluateLayer(plan.FeatureSize, step))
                {
                    continue;
                }

                (int octaves, float fade) = _lod.Resolve(plan.Frequency, plan.Lacunarity, plan.Octaves, step);

                resolved.Add(new ResolvedLayer
                {
                    Kernel = plan.Kernel,
                    Fractal = plan.Fractal,
                    Seed = plan.Seed,
                    Frequency = plan.Frequency,
                    Blend = plan.Blend,
                    Amplitude = plan.Amplitude,
                    Offset = plan.Offset,
                    BlendFactor = plan.BlendFactor,
                    Octaves = octaves,
                    LastOctaveFade = fade,
                });
            }

            ResolvedLayer[] result = resolved.ToArray();
            _resolvedByStep[step] = result;
            return result;
        }
    }

    /// <summary>A layer, flattened at compile time. Everything except what depends on sample spacing.</summary>
    private readonly struct LayerPlan
    {
        public required KernelConfig Kernel { get; init; }

        public required FractalConfig Fractal { get; init; }

        public required int Seed { get; init; }

        public required float Frequency { get; init; }

        public required float Lacunarity { get; init; }

        public required int Octaves { get; init; }

        public required LayerBlend Blend { get; init; }

        public required float Amplitude { get; init; }

        public required float Offset { get; init; }

        public required float BlendFactor { get; init; }

        public required float FeatureSize { get; init; }

        public string? Name { get; init; }
    }
}
