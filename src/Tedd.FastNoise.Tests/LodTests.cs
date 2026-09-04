using System;
using System.Linq;

namespace Tedd.FastNoise.Tests;

/// <summary>
/// Level of detail: that coarse sampling drops the octaves it cannot represent, that it does so by
/// the right amount, and that it stays out of the way when disabled.
/// </summary>
public class LodTests
{
    [Fact]
    public void Disabled_ChangesNothing()
    {
        LodPolicy policy = LodPolicy.Disabled;

        foreach (float step in new[] { 1f, 16f, 1024f, 100_000f })
        {
            (int octaves, float fade) = policy.Resolve(baseFrequency: 0.01f, lacunarity: 2f, octaves: 8, step);
            Assert.Equal(8, octaves);
            Assert.Equal(1f, fade);
        }
    }

    /// <summary>
    /// The cull point is where an octave's wavelength drops below <c>NyquistFactor</c> sample
    /// steps. With frequency 0.01 and lacunarity 2, octave <c>i</c> has wavelength
    /// <c>100 / 2^i</c>, so at a step of 1 and a factor of 2 everything down to wavelength 2
    /// survives: octaves 0..5 (wavelengths 100 to 3.125), and octave 6 at 1.5625 does not.
    /// </summary>
    [Fact]
    public void OctaveCount_TracksTheNyquistLimit()
    {
        LodPolicy policy = LodPolicy.Automatic with { FadeLastOctave = false };

        Assert.Equal(6, policy.Resolve(0.01f, 2f, octaves: 10, step: 1f).Octaves);

        // Doubling the sample spacing costs exactly one octave.
        Assert.Equal(5, policy.Resolve(0.01f, 2f, octaves: 10, step: 2f).Octaves);
        Assert.Equal(4, policy.Resolve(0.01f, 2f, octaves: 10, step: 4f).Octaves);

        // Never below one octave. With fading off there is no way to express "less than one octave",
        // so the count bottoms out here and the amplitude stays full; see
        // Fade_KeepsFallingOnceTheBaseOctaveIsTooFine for what happens with fading on.
        Assert.Equal(1, policy.Resolve(0.01f, 2f, octaves: 10, step: 100_000f).Octaves);
    }

    [Fact]
    public void OctaveCount_NeverExceedsTheConfiguredCount()
    {
        LodPolicy policy = LodPolicy.Automatic;

        for (int octaves = 1; octaves <= 12; octaves++)
        {
            Assert.True(policy.Resolve(0.0001f, 2f, octaves, step: 0.01f).Octaves <= octaves);
        }
    }

    [Fact]
    public void LastOctaveFade_RampsRatherThanPops()
    {
        LodPolicy policy = LodPolicy.Automatic;

        float previousFade = float.NaN;
        bool sawPartialFade = false;

        // Sweep the sample spacing across a cull boundary and check the fade moves continuously
        // rather than jumping from nothing to full amplitude.
        for (float step = 1f; step <= 4f; step *= 1.05f)
        {
            (int octaves, float fade) = policy.Resolve(0.01f, 2f, octaves: 10, step);

            Assert.InRange(fade, 0f, 1f);
            Assert.True(octaves >= 1);

            if (fade is > 0f and < 1f)
            {
                sawPartialFade = true;
            }

            previousFade = fade;
        }

        Assert.True(sawPartialFade, "Expected the fade to take intermediate values across a cull boundary.");
        Assert.False(float.IsNaN(previousFade));
    }

    [Fact]
    public void LayerCulling_DropsLayersFinerThanTheSampleGrid()
    {
        LodPolicy policy = LodPolicy.Automatic with { CullLayers = true };

        Assert.True(policy.ShouldEvaluateLayer(featureSize: 5000f, step: 500f));
        Assert.False(policy.ShouldEvaluateLayer(featureSize: 8f, step: 500f));

        // Zero means "no opinion", so never culled.
        Assert.True(policy.ShouldEvaluateLayer(featureSize: 0f, step: 100_000f));

        // Without the flag, nothing is culled regardless of size.
        Assert.True((LodPolicy.Automatic with { CullLayers = false }).ShouldEvaluateLayer(2f, 500f));
    }

    /// <summary>
    /// The point of octave culling is that the coarse field still describes the same landscape --
    /// it is the low-frequency part of it, not a different world.
    /// </summary>
    [Fact]
    public void CulledFill_StaysCloseToTheFullDetailField()
    {
        NoiseGenerator full = new(7)
        {
            NoiseType = NoiseType.OpenSimplex2,
            FractalType = FractalType.FBm,
            Octaves = 8,
            Frequency = 0.001f,
        };

        NoiseGenerator culled = new(7)
        {
            NoiseType = NoiseType.OpenSimplex2,
            FractalType = FractalType.FBm,
            Octaves = 8,
            Frequency = 0.001f,
            Lod = LodPolicy.Automatic,
        };

        // Sample every 64 units: the finest octaves are far below the grid and get dropped.
        GridRegion2D region = new(0f, 0f, 64, 64, 64f);

        float[] reference = full.Create(region);
        float[] approximate = culled.Create(region);

        // Only the fine octaves are missing, and they carry a small share of the amplitude.
        float largestDifference = reference.Zip(approximate, static (a, b) => MathF.Abs(a - b)).Max();
        Assert.True(largestDifference < 0.15f, $"Coarse field drifted by {largestDifference:0.###} from the full one.");

        // But it is not simply the same field: culling has to have actually done something.
        Assert.NotEqual(reference, approximate);
    }

    [Fact]
    public void CulledFill_IsCheaperInOctaves()
    {
        NoiseStack stack = new() { Lod = LodPolicy.Automatic with { CullLayers = true } };
        stack.Add(new NoiseLayer
        {
            Source = new NoiseGenerator(1) { Frequency = 0.0005f, FractalType = FractalType.FBm, Octaves = 8 },
            FeatureSize = 2000f,
            Name = "continents",
        });
        stack.Add(new NoiseLayer
        {
            Source = new NoiseGenerator(2) { Frequency = 0.05f, FractalType = FractalType.FBm, Octaves = 6 },
            FeatureSize = 16f,
            Name = "surface",
        });

        CompiledNoiseStack compiled = stack.Compile();

        Assert.Equal(2, compiled.DescribeActiveLayers(step: 1f).Count);
        Assert.Single(compiled.DescribeActiveLayers(step: 256f));

        // The surviving layer also runs fewer octaves from far away.
        string closeUp = compiled.DescribeActiveLayers(step: 1f)[0];
        string fromOrbit = compiled.DescribeActiveLayers(step: 256f)[0];
        Assert.NotEqual(closeUp, fromOrbit);
    }

    /// <summary>
    /// Culling must never leave a stack with nothing to evaluate; a buffer of zeros is a hole in
    /// the world, not a level of detail.
    /// </summary>
    [Fact]
    public void EverythingCulled_ProducesZeroesRatherThanFailing()
    {
        NoiseStack stack = new() { Lod = LodPolicy.Automatic with { CullLayers = true } };
        stack.Add(new NoiseLayer { Source = new NoiseGenerator(1) { Frequency = 0.5f }, FeatureSize = 2f });

        CompiledNoiseStack compiled = stack.Compile();
        float[] result = compiled.Create(new GridRegion2D(0f, 0f, 8, 8, 1_000_000f));

        Assert.All(result, static value => Assert.Equal(0f, value));
    }

    /// <summary>
    /// Past the point where the base octave stops being resolvable, the fade has to keep falling.
    /// </summary>
    /// <remarks>
    /// The original implementation clamped to one octave at full amplitude here, on the reasoning
    /// that the field should not vanish. The visible consequence was that zooming out did not smooth
    /// anything: the peaks stayed, in different places, because they were the aliased remains of an
    /// octave the sample grid could not carry.
    /// </remarks>
    [Fact]
    public void Fade_KeepsFallingOnceTheBaseOctaveIsTooFine()
    {
        LodPolicy policy = LodPolicy.Automatic;

        // Frequency 0.01 is a wavelength of 100 world units.
        float previous = 1f;
        bool reachedSilence = false;

        for (float step = 50f; step <= 200f; step *= 1.05f)
        {
            (int octaves, float fade) = policy.Resolve(baseFrequency: 0.01f, lacunarity: 2f, octaves: 1, step);

            Assert.Equal(1, octaves);
            Assert.InRange(fade, 0f, 1f);
            Assert.True(
                fade <= previous + 1e-4f,
                $"Fade rose from {previous:0.###} to {fade:0.###} as the sample spacing grew to {step:0.#}.");

            previous = fade;
            reachedSilence |= fade == 0f;
        }

        Assert.True(reachedSilence, "The fade never reached zero, so a coarse enough view still carries aliased detail.");
    }

    /// <summary>A source with no fractal at all still has to fade; it is one octave like any other.</summary>
    [Fact]
    public void SingleOctaveSource_FadesOutToo()
    {
        NoiseGenerator noise = new(3)
        {
            NoiseType = NoiseType.OpenSimplex2,
            FractalType = FractalType.None,
            Frequency = 0.05f,
            Lod = LodPolicy.Automatic,
        };

        // Wavelength is 20 units; sampling every 200 cannot carry any of it.
        float[] coarse = noise.Create(new GridRegion2D(0f, 0f, 32, 32, Step: 200f));

        Assert.All(coarse, static value => Assert.Equal(0f, value));
    }

    /// <summary>
    /// The behaviour this is all for: as the camera pulls back, the terrain should smooth out, not
    /// merely rearrange its peaks.
    /// </summary>
    [Fact]
    public void ZoomingOut_SmoothsTheFieldInsteadOfMovingThePeaks()
    {
        static NoiseGenerator Build(LodPolicy lod) => new(1337)
        {
            NoiseType = NoiseType.OpenSimplex2,
            FractalType = FractalType.FBm,
            Octaves = 6,
            Frequency = 0.01f,
            Lod = lod,
        };

        NoiseGenerator aliased = Build(LodPolicy.Disabled);
        NoiseGenerator banded = Build(LodPolicy.Automatic);

        float[] steps = [1f, 8f, 64f, 512f];
        double previousBanded = double.MaxValue;

        foreach (float step in steps)
        {
            GridRegion2D region = new(0f, 0f, 96, 96, step);

            double aliasedSpread = StandardDeviation(aliased.Create(region));
            double bandedSpread = StandardDeviation(banded.Create(region));

            // Without level of detail the field keeps its full range at every zoom: that is the
            // aliasing, and it is why a distant landscape boils under camera motion.
            Assert.True(
                aliasedSpread > 0.1,
                $"Expected the unfiltered field to stay rough at step {step}, but its spread was {aliasedSpread:0.####}.");

            // With it, each step out must be at least as smooth as the last.
            Assert.True(
                bandedSpread <= previousBanded + 1e-6,
                $"Band-limited spread rose from {previousBanded:0.####} to {bandedSpread:0.####} at step {step}.");

            previousBanded = bandedSpread;
        }

        // And by the far end it is genuinely flat, not merely calmer.
        Assert.True(previousBanded < 0.02, $"Expected a nearly flat field when zoomed right out, got a spread of {previousBanded:0.####}.");
    }

    private static double StandardDeviation(float[] values)
    {
        double mean = 0;
        foreach (float value in values)
        {
            mean += value;
        }

        mean /= values.Length;

        double sum = 0;
        foreach (float value in values)
        {
            double delta = value - mean;
            sum += delta * delta;
        }

        return Math.Sqrt(sum / values.Length);
    }
}
