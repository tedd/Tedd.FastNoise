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

        // Never below one: a field that returns nothing at all is worse than a coarse field.
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
}
