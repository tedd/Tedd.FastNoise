using System;

namespace Tedd.FastNoise;

/// <summary>
/// Controls how much detail a fill actually computes, given how far apart its samples are.
/// </summary>
/// <remarks>
/// <para>
/// Sampling an eight-octave fractal every 512 world units is not just wasteful, it is wrong. The
/// finest octaves have a wavelength far below the sample spacing, so what lands in the buffer is
/// aliasing: a random-looking value with no relationship to the terrain those octaves describe.
/// Fly the camera in and the aliased values are replaced by different aliased values, and the
/// distant landscape boils.
/// </para>
/// <para>
/// This policy drops octaves that cannot be represented at the requested sample spacing. The
/// result is cheaper (a planet-scale view may run one octave instead of eight) and more stable
/// (what remains is the part of the signal the sample grid can actually carry). It is the same
/// argument as mipmapping a texture, applied to a procedural function.
/// </para>
/// <para>
/// Off by default. With it off, output is bit-identical to FastNoiseLite for any step; with it on,
/// coarse fills deliberately differ from a decimated fine fill, because that is the point.
/// </para>
/// </remarks>
public readonly record struct LodPolicy
{
    /// <summary>Compute every configured octave regardless of sample spacing. The default.</summary>
    public static LodPolicy Disabled => default;

    /// <summary>Drop octaves finer than the sample grid can represent, and fade the last one in smoothly.</summary>
    public static LodPolicy Automatic => new()
    {
        CullOctaves = true,
        NyquistFactor = 2f,
        FadeLastOctave = true,
    };

    /// <summary>Whether to drop octaves whose wavelength falls below the sample spacing.</summary>
    public bool CullOctaves { get; init; }

    /// <summary>
    /// How many samples per wavelength an octave needs to survive. 2 is the Nyquist limit and keeps
    /// the most detail; 3 or 4 trade detail for a quieter, more stable image under camera motion.
    /// </summary>
    public float NyquistFactor { get; init; }

    /// <summary>
    /// Fade the finest surviving octave in and out by fractional amplitude rather than switching it
    /// on whole. Without this, terrain visibly pops as an octave crosses the cull threshold.
    /// </summary>
    public bool FadeLastOctave { get; init; }

    /// <summary>Whether to skip layers whose feature size is smaller than the sample spacing.</summary>
    /// <remarks>
    /// Complements octave culling for stacks built from named layers -- a "surface pebbles" layer
    /// has nothing to contribute to a view from orbit and can be skipped entirely rather than
    /// reduced to one octave. See <see cref="NoiseLayer.FeatureSize"/>.
    /// </remarks>
    public bool CullLayers { get; init; }

    /// <summary>
    /// Resolves how many octaves to run and how loudly to play the last one.
    /// </summary>
    /// <param name="baseFrequency">Frequency of the first octave, in cycles per world unit.</param>
    /// <param name="lacunarity">Frequency multiplier between octaves.</param>
    /// <param name="octaves">Octave count at full detail.</param>
    /// <param name="step">World units between samples.</param>
    /// <returns>The octave count to run, and an amplitude multiplier in (0, 1] for the final octave.</returns>
    public (int Octaves, float LastOctaveFade) Resolve(float baseFrequency, float lacunarity, int octaves, float step)
    {
        if (!CullOctaves || octaves <= 1 || lacunarity <= 1f || baseFrequency <= 0f || !float.IsFinite(step))
        {
            return (octaves, 1f);
        }

        float nyquist = NyquistFactor > 0f ? NyquistFactor : 2f;

        // An octave survives while its wavelength is at least `nyquist` sample steps:
        //   1 / (baseFrequency * lacunarity^i) >= nyquist * step
        // Solving for i gives the count directly, without walking the octaves.
        float limit = 1f / (nyquist * step * baseFrequency);
        if (limit <= 1f)
        {
            // Even the base octave is below the sample grid. Keep one so the field does not vanish.
            return (1, 1f);
        }

        float exact = MathF.Log(limit) / MathF.Log(lacunarity) + 1f;
        int usable = Math.Clamp((int)exact, 1, octaves);

        if (!FadeLastOctave || usable >= octaves)
        {
            return (usable, 1f);
        }

        // Fractional part of the cutoff: how far the next octave is from becoming representable.
        // Ramping the final octave's amplitude across that interval removes the pop.
        float fade = exact - MathF.Floor(exact);
        return (usable, fade <= 0f ? 1f : fade);
    }

    /// <summary>Whether a layer with the given smallest feature size is worth evaluating at this sample spacing.</summary>
    /// <param name="featureSize">The layer's smallest meaningful feature, in world units. Zero or less means "always evaluate".</param>
    /// <param name="step">World units between samples.</param>
    public bool ShouldEvaluateLayer(float featureSize, float step)
    {
        if (!CullLayers || featureSize <= 0f)
        {
            return true;
        }

        float nyquist = NyquistFactor > 0f ? NyquistFactor : 2f;
        return featureSize >= nyquist * step;
    }
}
