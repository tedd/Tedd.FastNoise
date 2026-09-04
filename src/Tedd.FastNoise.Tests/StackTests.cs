using System;
using System.Collections.Generic;
using System.Linq;

namespace Tedd.FastNoise.Tests;

/// <summary>
/// Layer stacking: that the fused fill computes what the layer definitions say it should,
/// and that fusing does not change the answer.
/// </summary>
public class StackTests
{
    private static NoiseGenerator Source(int seed, NoiseType type = NoiseType.OpenSimplex2, float frequency = 0.01f)
        => new(seed) { NoiseType = type, Frequency = frequency };

    /// <summary>
    /// The definition of the stack, computed the slow obvious way. If the fused loop disagrees with
    /// this, the fusion is wrong.
    /// </summary>
    private static float BlendByHand(IReadOnlyList<NoiseLayer> layers, float x, float y)
    {
        float accumulator = 0f;

        for (int index = 0; index < layers.Count; index++)
        {
            NoiseLayer layer = layers[index];
            float value = (layer.Source.GetNoise(x, y) * layer.Amplitude) + layer.Offset;

            accumulator = index == 0
                ? value
                : layer.Blend switch
                {
                    LayerBlend.Add => accumulator + value,
                    LayerBlend.Subtract => accumulator - value,
                    LayerBlend.Multiply => accumulator * value,
                    LayerBlend.Min => MathF.Min(accumulator, value),
                    LayerBlend.Max => MathF.Max(accumulator, value),
                    LayerBlend.Replace => value,
                    LayerBlend.Lerp => accumulator + (layer.BlendFactor * (value - accumulator)),
                    _ => accumulator + value,
                };
        }

        return accumulator;
    }

    [Theory]
    [InlineData(LayerBlend.Add)]
    [InlineData(LayerBlend.Subtract)]
    [InlineData(LayerBlend.Multiply)]
    [InlineData(LayerBlend.Min)]
    [InlineData(LayerBlend.Max)]
    [InlineData(LayerBlend.Replace)]
    [InlineData(LayerBlend.Lerp)]
    public void EveryBlend_MatchesTheHandComputedDefinition(LayerBlend blend)
    {
        NoiseStack stack = new();
        stack.Add(new NoiseLayer { Source = Source(1), Amplitude = 1f });
        stack.Add(new NoiseLayer { Source = Source(2, NoiseType.Perlin, 0.03f), Blend = blend, Amplitude = 0.5f, Offset = 0.25f, BlendFactor = 0.3f });
        stack.Add(new NoiseLayer { Source = Source(3, NoiseType.Value, 0.08f), Blend = blend, Amplitude = 0.25f, BlendFactor = 0.7f });

        CompiledNoiseStack compiled = stack.Compile();
        Random random = new(Seed: 606);

        for (int i = 0; i < 2_000; i++)
        {
            float x = ((float)random.NextDouble() - 0.5f) * 500f;
            float y = ((float)random.NextDouble() - 0.5f) * 500f;

            Assert.Equal(BlendByHand(stack.Layers, x, y), compiled.GetNoise(x, y));
        }
    }

    [Fact]
    public void FusedFill_MatchesPointSampling()
    {
        NoiseStack stack = new();
        stack.Add(new NoiseLayer { Source = Source(11, NoiseType.OpenSimplex2, 0.002f), Amplitude = 1f, Name = "continents" });
        stack.Add(new NoiseLayer { Source = Source(12, NoiseType.Perlin, 0.02f), Blend = LayerBlend.Add, Amplitude = 0.3f, Name = "hills" });
        stack.Add(new NoiseLayer { Source = Source(13, NoiseType.Cellular, 0.05f), Blend = LayerBlend.Max, Amplitude = 0.2f, Name = "outcrops" });
        stack.Add(new NoiseLayer { Source = Source(14, NoiseType.Value, 0.2f), Blend = LayerBlend.Add, Amplitude = 0.05f, Name = "grain" });

        CompiledNoiseStack compiled = stack.Compile();

        // A width that leaves a scalar tail on every plausible vector width.
        GridRegion2D region = new(-40.5f, 12.25f, 61, 23, 0.5f);
        float[] filled = compiled.Create(region);

        for (int y = 0; y < region.Height; y++)
        {
            for (int x = 0; x < region.Width; x++)
            {
                float expected = compiled.GetNoise(
                    region.OriginX + (x * region.Step),
                    region.OriginY + (y * region.Step));

                Assert.Equal(expected, filled[x + (y * region.Width)]);
            }
        }
    }

    [Fact]
    public void FusedFill_IsIdenticalAcrossBackends()
    {
        NoiseStack stack = new() { ParallelThreshold = 1 };
        stack.Add(new NoiseLayer { Source = Source(21, NoiseType.OpenSimplex2, 0.004f) });
        stack.Add(new NoiseLayer { Source = Source(22, NoiseType.Perlin, 0.04f), Amplitude = 0.4f });
        stack.Add(new NoiseLayer { Source = Source(23, NoiseType.ValueCubic, 0.09f), Blend = LayerBlend.Multiply, Amplitude = 0.5f, Offset = 0.5f });

        CompiledNoiseStack compiled = stack.Compile();
        GridRegion3D region = new(3.5f, -2f, 7.25f, 23, 9, 5, 0.75f);

        float[] baseline = compiled.Create(region, NoiseBackend.Scalar);

        foreach (NoiseBackend backend in new[] { NoiseBackend.Simd, NoiseBackend.Parallel, NoiseBackend.Auto })
        {
            float[] actual = compiled.Create(region, backend);
            Assert.Equal(baseline, actual);
        }
    }

    /// <summary>
    /// A stack containing OpenSimplex2S cannot vectorise, but it still has to produce the right
    /// answer, and it still has to be able to run in parallel.
    /// </summary>
    [Fact]
    public void StackWithScalarOnlyLayer_StillCorrect()
    {
        NoiseStack stack = new() { ParallelThreshold = 1 };
        stack.Add(new NoiseLayer { Source = Source(31, NoiseType.OpenSimplex2S, 0.01f) });
        stack.Add(new NoiseLayer { Source = Source(32, NoiseType.Perlin, 0.05f), Amplitude = 0.5f });

        CompiledNoiseStack compiled = stack.Compile();
        Assert.False(compiled.IsVectorised);

        GridRegion2D region = new(0f, 0f, 37, 11);
        float[] sequential = compiled.Create(region, NoiseBackend.Scalar);
        float[] parallel = compiled.Create(region, NoiseBackend.Parallel);

        Assert.Equal(sequential, parallel);

        for (int y = 0; y < region.Height; y++)
        {
            for (int x = 0; x < region.Width; x++)
            {
                Assert.Equal(compiled.GetNoise(x, y), sequential[x + (y * region.Width)]);
            }
        }
    }

    [Fact]
    public void CompiledStack_IsASnapshot()
    {
        NoiseGenerator source = Source(41, NoiseType.Perlin, 0.01f);
        NoiseStack stack = new();
        stack.Add(new NoiseLayer { Source = source });

        CompiledNoiseStack compiled = stack.Compile();
        float before = compiled.GetNoise(10f, 20f);

        // Mutating the generator the layer was built from must not reach through.
        source.Frequency = 0.5f;
        source.Seed = 999;

        Assert.Equal(before, compiled.GetNoise(10f, 20f));

        // Recompiling picks the change up.
        Assert.NotEqual(before, stack.Compile().GetNoise(10f, 20f));
    }

    [Fact]
    public void EmptyStack_CannotCompile()
        => Assert.Throws<InvalidOperationException>(() => new NoiseStack().Compile());

    [Fact]
    public void Add_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => new NoiseStack().Add((NoiseLayer)null!));

    [Fact]
    public void SingleLayerStack_EqualsTheGeneratorItself()
    {
        NoiseGenerator source = Source(51, NoiseType.OpenSimplex2, 0.02f);
        source.FractalType = FractalType.FBm;
        source.Octaves = 4;

        CompiledNoiseStack compiled = new NoiseStack().Add(source).Compile();
        Random random = new(Seed: 12);

        for (int i = 0; i < 1_000; i++)
        {
            float x = ((float)random.NextDouble() - 0.5f) * 300f;
            float y = ((float)random.NextDouble() - 0.5f) * 300f;
            float z = ((float)random.NextDouble() - 0.5f) * 300f;

            Assert.Equal(source.GetNoise(x, y), compiled.GetNoise(x, y));
            Assert.Equal(source.GetNoise(x, y, z), compiled.GetNoise(x, y, z));
        }
    }

    [Fact]
    public void DescribeActiveLayers_NamesWhatWillRun()
    {
        NoiseStack stack = new() { Lod = LodPolicy.Automatic with { CullLayers = true } };
        stack.Add(new NoiseLayer { Source = Source(61, NoiseType.Perlin, 0.0002f), FeatureSize = 5000f, Name = "continents" });
        stack.Add(new NoiseLayer { Source = Source(62, NoiseType.Perlin, 0.05f), FeatureSize = 20f, Name = "boulders" });

        Assert.Equal(2, stack.Compile().DescribeActiveLayers(step: 1f).Count);

        IReadOnlyList<string> fromOrbit = stack.Compile().DescribeActiveLayers(step: 500f);
        Assert.Single(fromOrbit);
        Assert.Contains("continents", fromOrbit[0], StringComparison.Ordinal);
    }
}
