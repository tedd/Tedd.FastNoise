using System;
using System.Collections.Generic;
using System.Linq;
using Tedd.FastNoise.Internal;

namespace Tedd.FastNoise;

/// <summary>
/// An ordered set of <see cref="NoiseLayer"/>s evaluated together as one noise function.
/// </summary>
/// <remarks>
/// <para>
/// This is the builder. <see cref="Compile"/> turns it into a <see cref="CompiledNoiseStack"/>,
/// which is the thing that actually generates: immutable, thread-safe, and flattened so that a
/// fill runs every layer against coordinates held in registers instead of writing a buffer per
/// layer. Sampling a stack directly compiles it once and caches the result, so the first call
/// after a mutation pays for the compile and the rest do not.
/// </para>
/// <example>
/// A world built in layers, coarsest first:
/// <code>
/// var stack = new NoiseStack
/// {
///     Lod = LodPolicy.Automatic with { CullLayers = true },
/// };
///
/// stack.Add(new NoiseLayer                       // continents
/// {
///     Source = new NoiseGenerator(1) { Frequency = 0.0002f, FractalType = FractalType.FBm, Octaves = 4 },
///     Amplitude = 1f,
///     FeatureSize = 2000f,
///     Name = "continents",
/// });
///
/// stack.Add(new NoiseLayer                       // mountains, only where continents are high
/// {
///     Source = new NoiseGenerator(2) { Frequency = 0.002f, FractalType = FractalType.Ridged, Octaves = 5 },
///     Blend = LayerBlend.Add,
///     Amplitude = 0.4f,
///     FeatureSize = 200f,
///     Name = "mountains",
/// });
///
/// stack.Add(new NoiseLayer                       // surface roughness, invisible from orbit
/// {
///     Source = new NoiseGenerator(3) { Frequency = 0.05f, NoiseType = NoiseType.Value },
///     Amplitude = 0.02f,
///     FeatureSize = 8f,
///     Name = "surface detail",
/// });
///
/// var compiled = stack.Compile();
/// compiled.Fill(heights, new GridRegion2D(0, 0, 512, 512, Step: 1f));      // walking: all layers
/// compiled.Fill(overview, new GridRegion2D(0, 0, 512, 512, Step: 512f));   // orbit: continents only
/// </code>
/// </example>
/// </remarks>
public sealed class NoiseStack
{
    private readonly List<NoiseLayer> _layers = [];
    private CompiledNoiseStack? _compiled;
    private LodPolicy _lod = LodPolicy.Disabled;

    /// <summary>The layers, evaluated in order. The first one to survive level-of-detail culling initialises the accumulator.</summary>
    public IReadOnlyList<NoiseLayer> Layers => _layers;

    /// <summary>How much detail fills of this stack compute, given their sample spacing.</summary>
    /// <remarks>
    /// Applies to every layer, and enables <see cref="NoiseLayer.FeatureSize"/> culling when
    /// <see cref="LodPolicy.CullLayers"/> is set.
    /// </remarks>
    public LodPolicy Lod
    {
        get => _lod;
        set
        {
            _lod = value;
            _compiled = null;
        }
    }

    /// <summary>Sample count from which fills of this stack use every core.</summary>
    public int ParallelThreshold { get; set; } = 16384;

    /// <summary>Appends a layer.</summary>
    /// <param name="layer">The layer to add.</param>
    /// <returns>This stack, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layer"/> is null.</exception>
    public NoiseStack Add(NoiseLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _layers.Add(layer);
        _compiled = null;
        return this;
    }

    /// <summary>Appends a generator as an additive layer at full amplitude.</summary>
    /// <param name="source">The generator to add.</param>
    /// <param name="amplitude">Multiplier applied to its output.</param>
    /// <param name="featureSize">Smallest feature in world units, for level-of-detail culling, or 0 to never cull.</param>
    /// <param name="name">Optional name, for diagnostics.</param>
    /// <returns>This stack, for chaining.</returns>
    public NoiseStack Add(NoiseGenerator source, float amplitude = 1f, float featureSize = 0f, string? name = null)
        => Add(new NoiseLayer
        {
            Source = source,
            Amplitude = amplitude,
            FeatureSize = featureSize,
            Name = name,
        });

    /// <summary>Removes every layer.</summary>
    public void Clear()
    {
        _layers.Clear();
        _compiled = null;
    }

    /// <summary>
    /// Flattens the stack into an immutable, thread-safe executor.
    /// </summary>
    /// <returns>A compiled stack that generates without touching these layer objects again.</returns>
    /// <exception cref="InvalidOperationException">The stack has no layers.</exception>
    /// <remarks>
    /// <para>
    /// Compiling copies each layer's settings into a flat record and resolves everything that does
    /// not depend on the sample spacing. What remains at fill time is one array walk per vector of
    /// coordinates.
    /// </para>
    /// <para>
    /// Every call takes a fresh snapshot. Mutating this stack afterwards, or mutating a
    /// <see cref="NoiseGenerator"/> a layer points at, does not affect a stack already compiled.
    /// That is deliberate: a compiled stack can be handed to worker threads without worrying about
    /// what the main thread is doing to the builder.
    /// </para>
    /// </remarks>
    public CompiledNoiseStack Compile()
    {
        if (_layers.Count == 0)
        {
            throw new InvalidOperationException("A noise stack needs at least one layer before it can be compiled.");
        }

        return new CompiledNoiseStack(_layers.ToArray(), _lod, ParallelThreshold);
    }

    /// <summary>
    /// The compiled stack behind the convenience sampling methods.
    /// </summary>
    /// <remarks>
    /// Invalidated when this stack changes, but it cannot see through to the
    /// <see cref="NoiseGenerator"/>s the layers point at. Mutate one of those and the convenience
    /// methods keep using the snapshot taken before the change; call <see cref="Compile"/> for a
    /// fresh one. Compiling once and keeping the result is the intended usage anyway.
    /// </remarks>
    private CompiledNoiseStack Cached() => _compiled ??= Compile();

    /// <summary>Samples the whole stack at one 2D point.</summary>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    public float GetNoise(float x, float y) => Cached().GetNoise(x, y);

    /// <summary>Samples the whole stack at one 3D point.</summary>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    /// <param name="z">World Z.</param>
    public float GetNoise(float x, float y, float z) => Cached().GetNoise(x, y, z);

    /// <summary>Fills <paramref name="destination"/> with the whole stack over <paramref name="region"/>.</summary>
    /// <param name="destination">Receives <c>region.SampleCount</c> values, X-fastest.</param>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy.</param>
    public void Fill(Span<float> destination, in GridRegion2D region, NoiseBackend backend = NoiseBackend.Auto)
        => Cached().Fill(destination, region, backend);

    /// <summary>Fills <paramref name="destination"/> with the whole stack over <paramref name="region"/>.</summary>
    /// <param name="destination">Receives <c>region.SampleCount</c> values, X-fastest then Y then Z.</param>
    /// <param name="region">Where to sample and how densely.</param>
    /// <param name="backend">Execution strategy.</param>
    public void Fill(Span<float> destination, in GridRegion3D region, NoiseBackend backend = NoiseBackend.Auto)
        => Cached().Fill(destination, region, backend);

    /// <inheritdoc />
    public override string ToString()
        => $"NoiseStack [{string.Join(" | ", _layers.Select(static layer => layer.ToString()))}]";
}
