using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal.Kernels;

/// <summary>Everything the octave loop needs. Copied into fills, never mutated during one.</summary>
internal readonly struct FractalConfig
{
    /// <summary>How octaves combine, or <see cref="FractalType.None"/> for a single octave.</summary>
    public required FractalType Type { get; init; }

    /// <summary>Number of octaves at full detail. Fills may run fewer; see <c>LodPolicy</c>.</summary>
    public required int Octaves { get; init; }

    /// <summary>Frequency multiplier between octaves. 2.0 doubles the frequency each step.</summary>
    public required float Lacunarity { get; init; }

    /// <summary>Amplitude multiplier between octaves. 0.5 halves the contribution each step.</summary>
    public required float Gain { get; init; }

    /// <summary>Biases later octaves toward the peaks of earlier ones, roughening high ground and smoothing low.</summary>
    public float WeightedStrength { get; init; }

    /// <summary>Number of folds per octave for <see cref="FractalType.PingPong"/>.</summary>
    public float PingPongStrength { get; init; }

    /// <summary>
    /// Reciprocal of the summed octave amplitudes, so the result stays inside [-1, 1].
    /// </summary>
    /// <remarks>
    /// Computed from the full octave count and deliberately not recomputed when level of detail
    /// drops octaves. Renormalising would make the coarse rendering of a landscape a different
    /// height from the fine one -- terrain would visibly breathe as you flew toward it.
    /// </remarks>
    public required float Bounding { get; init; }
}

/// <summary>
/// The octave loop: sample the base noise repeatedly at rising frequency and falling amplitude.
/// </summary>
/// <remarks>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md.
/// The small inconsistencies between the 2D and 3D weighting are the reference's, kept for
/// compatibility rather than tidied.
/// </remarks>
internal static class FractalKernel
{
    /// <summary>Runs <paramref name="octaves"/> octaves in 2D over already-transformed coordinates.</summary>
    /// <param name="kernel">Which algorithm each octave runs.</param>
    /// <param name="fractal">Octave count, falloff and shaping at full detail.</param>
    /// <param name="seed">Seed of the first octave; each subsequent octave adds one.</param>
    /// <param name="x">Transformed X coordinate.</param>
    /// <param name="y">Transformed Y coordinate.</param>
    /// <param name="octaves">Octaves to actually run, at or below <see cref="FractalConfig.Octaves"/>.</param>
    /// <param name="lastOctaveFade">Amplitude multiplier for the final octave, in (0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Fractal2<TOps, TF, TI>(
        in KernelConfig kernel, in FractalConfig fractal, int seed, TF x, TF y, int octaves, float lastOctaveFade)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        if (fractal.Type == FractalType.None || octaves <= 1)
        {
            TF single = NoisePipeline.Single2<TOps, TF, TI>(kernel, TOps.I(seed), x, y);

            // The fade applies to a plain single-octave source too, not just to a fractal that level
            // of detail has worn down to one octave. Without this a non-fractal layer keeps full
            // amplitude at every zoom, which is exactly the layer whose detail should disappear first.
            TF shaped = fractal.Type == FractalType.None ? single : ShapeSingle<TOps, TF, TI>(fractal, single);
            return TOps.Mul(shaped, TOps.F(lastOctaveFade));
        }

        TF sum = TOps.F(0f);
        TF amp = TOps.F(fractal.Bounding);
        TF lacunarity = TOps.F(fractal.Lacunarity);
        TF gain = TOps.F(fractal.Gain);
        TF weighted = TOps.F(fractal.WeightedStrength);

        for (int octave = 0; octave < octaves; octave++)
        {
            // Level of detail may ask for the finest surviving octave at reduced amplitude, so it
            // ramps in rather than popping into existence as the camera approaches.
            if (lastOctaveFade != 1f && octave == octaves - 1)
            {
                amp = TOps.Mul(amp, TOps.F(lastOctaveFade));
            }

            TF noise = NoisePipeline.Single2<TOps, TF, TI>(kernel, TOps.I(seed + octave), x, y);

            TF weight;
            switch (fractal.Type)
            {
                case FractalType.Ridged:
                    noise = TOps.Abs(noise);
                    sum = TOps.Add(sum, TOps.Mul(TOps.Add(TOps.Mul(noise, TOps.F(-2f)), TOps.F(1f)), amp));
                    weight = TOps.Sub(TOps.F(1f), noise);
                    break;

                case FractalType.PingPong:
                    noise = NoiseMath.PingPong<TOps, TF, TI>(
                        TOps.Mul(TOps.Add(noise, TOps.F(1f)), TOps.F(fractal.PingPongStrength)));
                    sum = TOps.Add(sum, TOps.Mul(TOps.Mul(TOps.Sub(noise, TOps.F(0.5f)), TOps.F(2f)), amp));
                    weight = noise;
                    break;

                default:
                    sum = TOps.Add(sum, TOps.Mul(noise, amp));
                    // 2D clamps the weight to 2 before halving; 3D does not. The reference's asymmetry.
                    weight = TOps.Mul(TOps.Min(TOps.Add(noise, TOps.F(1f)), TOps.F(2f)), TOps.F(0.5f));
                    break;
            }

            amp = TOps.Mul(amp, NoiseMath.Lerp<TOps, TF, TI>(TOps.F(1f), weight, weighted));
            x = TOps.Mul(x, lacunarity);
            y = TOps.Mul(y, lacunarity);
            amp = TOps.Mul(amp, gain);
        }

        return sum;
    }

    /// <summary>Runs <paramref name="octaves"/> octaves in 3D over already-transformed coordinates.</summary>
    /// <param name="kernel">Which algorithm each octave runs.</param>
    /// <param name="fractal">Octave count, falloff and shaping at full detail.</param>
    /// <param name="seed">Seed of the first octave; each subsequent octave adds one.</param>
    /// <param name="x">Transformed X coordinate.</param>
    /// <param name="y">Transformed Y coordinate.</param>
    /// <param name="z">Transformed Z coordinate.</param>
    /// <param name="octaves">Octaves to actually run, at or below <see cref="FractalConfig.Octaves"/>.</param>
    /// <param name="lastOctaveFade">Amplitude multiplier for the final octave, in (0, 1].</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Fractal3<TOps, TF, TI>(
        in KernelConfig kernel, in FractalConfig fractal, int seed, TF x, TF y, TF z, int octaves, float lastOctaveFade)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        if (fractal.Type == FractalType.None || octaves <= 1)
        {
            TF single = NoisePipeline.Single3<TOps, TF, TI>(kernel, TOps.I(seed), x, y, z);

            // The fade applies to a plain single-octave source too, not just to a fractal that level
            // of detail has worn down to one octave. Without this a non-fractal layer keeps full
            // amplitude at every zoom, which is exactly the layer whose detail should disappear first.
            TF shaped = fractal.Type == FractalType.None ? single : ShapeSingle<TOps, TF, TI>(fractal, single);
            return TOps.Mul(shaped, TOps.F(lastOctaveFade));
        }

        TF sum = TOps.F(0f);
        TF amp = TOps.F(fractal.Bounding);
        TF lacunarity = TOps.F(fractal.Lacunarity);
        TF gain = TOps.F(fractal.Gain);
        TF weighted = TOps.F(fractal.WeightedStrength);

        for (int octave = 0; octave < octaves; octave++)
        {
            // Level of detail may ask for the finest surviving octave at reduced amplitude, so it
            // ramps in rather than popping into existence as the camera approaches.
            if (lastOctaveFade != 1f && octave == octaves - 1)
            {
                amp = TOps.Mul(amp, TOps.F(lastOctaveFade));
            }

            TF noise = NoisePipeline.Single3<TOps, TF, TI>(kernel, TOps.I(seed + octave), x, y, z);

            TF weight;
            switch (fractal.Type)
            {
                case FractalType.Ridged:
                    noise = TOps.Abs(noise);
                    sum = TOps.Add(sum, TOps.Mul(TOps.Add(TOps.Mul(noise, TOps.F(-2f)), TOps.F(1f)), amp));
                    weight = TOps.Sub(TOps.F(1f), noise);
                    break;

                case FractalType.PingPong:
                    noise = NoiseMath.PingPong<TOps, TF, TI>(
                        TOps.Mul(TOps.Add(noise, TOps.F(1f)), TOps.F(fractal.PingPongStrength)));
                    sum = TOps.Add(sum, TOps.Mul(TOps.Mul(TOps.Sub(noise, TOps.F(0.5f)), TOps.F(2f)), amp));
                    weight = noise;
                    break;

                default:
                    sum = TOps.Add(sum, TOps.Mul(noise, amp));
                    weight = TOps.Mul(TOps.Add(noise, TOps.F(1f)), TOps.F(0.5f));
                    break;
            }

            amp = TOps.Mul(amp, NoiseMath.Lerp<TOps, TF, TI>(TOps.F(1f), weight, weighted));
            x = TOps.Mul(x, lacunarity);
            y = TOps.Mul(y, lacunarity);
            z = TOps.Mul(z, lacunarity);
            amp = TOps.Mul(amp, gain);
        }

        return sum;
    }

    /// <summary>
    /// Applies a fractal's per-octave shaping to a single octave.
    /// </summary>
    /// <remarks>
    /// Reached when level of detail has culled a stack down to one octave. Without this, a ridged
    /// stack seen from orbit would lose its ridges and turn into plain gradient noise as the
    /// octave count fell to one, which reads as the terrain melting.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TF ShapeSingle<TOps, TF, TI>(in FractalConfig fractal, TF noise)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TF amp = TOps.F(fractal.Bounding);

        switch (fractal.Type)
        {
            case FractalType.Ridged:
                return TOps.Mul(TOps.Add(TOps.Mul(TOps.Abs(noise), TOps.F(-2f)), TOps.F(1f)), amp);

            case FractalType.PingPong:
            {
                TF folded = NoiseMath.PingPong<TOps, TF, TI>(
                    TOps.Mul(TOps.Add(noise, TOps.F(1f)), TOps.F(fractal.PingPongStrength)));
                return TOps.Mul(TOps.Mul(TOps.Sub(folded, TOps.F(0.5f)), TOps.F(2f)), amp);
            }

            default:
                return TOps.Mul(noise, amp);
        }
    }
}
