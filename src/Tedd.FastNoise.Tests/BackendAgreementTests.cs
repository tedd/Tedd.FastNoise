using System;
using System.Numerics;

namespace Tedd.FastNoise.Tests;

/// <summary>
/// Every backend must produce the same bytes.
/// </summary>
/// <remarks>
/// This is the guarantee that makes the SIMD path usable in a networked game. A client with
/// AVX-512, a client with NEON and a headless server running the scalar fallback have to agree on
/// where the ground is, and "agree to within a small epsilon" is not agreement -- a float
/// comparison against a terrain threshold turns an ULP into a whole block of difference.
/// </remarks>
public class BackendAgreementTests
{
    /// <summary>Backends that must all produce identical output.</summary>
    private static readonly NoiseBackend[] Backends =
    [
        NoiseBackend.Scalar,
        NoiseBackend.Simd,
        NoiseBackend.Parallel,
        NoiseBackend.Auto,
    ];

    [Theory]
    [MemberData(nameof(NoiseCases.NoiseAndFractalTypes), MemberType = typeof(NoiseCases))]
    public void Fill2D_IsIdenticalAcrossBackends(NoiseType noiseType, FractalType fractalType)
    {
        NoiseGenerator noise = new(90210)
        {
            NoiseType = noiseType,
            FractalType = fractalType,
            Octaves = 4,
            Frequency = 0.013f,

            // Force the parallel path to actually partition rather than fall back on size.
            ParallelThreshold = 1,
        };

        // A width that is not a multiple of any vector width, so the scalar tail is exercised.
        GridRegion2D region = new(-137.25f, 61.5f, 67, 53, 0.75f);

        float[] baseline = noise.Create(region, NoiseBackend.Scalar);

        foreach (NoiseBackend backend in Backends)
        {
            float[] actual = noise.Create(region, backend);
            AssertIdentical(baseline, actual, $"{noiseType}/{fractalType} via {backend}");
        }
    }

    [Theory]
    [MemberData(nameof(NoiseCases.NoiseAndFractalTypes), MemberType = typeof(NoiseCases))]
    public void Fill3D_IsIdenticalAcrossBackends(NoiseType noiseType, FractalType fractalType)
    {
        NoiseGenerator noise = new(24)
        {
            NoiseType = noiseType,
            FractalType = fractalType,
            Octaves = 3,
            Frequency = 0.02f,
            ParallelThreshold = 1,
        };

        GridRegion3D region = new(11.5f, -4f, 900.25f, 19, 13, 7, 1.25f);

        float[] baseline = noise.Create(region, NoiseBackend.Scalar);

        foreach (NoiseBackend backend in Backends)
        {
            float[] actual = noise.Create(region, backend);
            AssertIdentical(baseline, actual, $"{noiseType}/{fractalType} via {backend}");
        }
    }

    /// <summary>
    /// A fill has to agree with point sampling, or the two APIs are different functions wearing
    /// the same settings.
    /// </summary>
    [Theory]
    [MemberData(nameof(NoiseCases.AllNoiseTypes), MemberType = typeof(NoiseCases))]
    public void Fill_AgreesWithPointSampling(NoiseType noiseType)
    {
        NoiseGenerator noise = new(31415)
        {
            NoiseType = noiseType,
            FractalType = FractalType.FBm,
            Octaves = 3,
            Frequency = 0.017f,
        };

        GridRegion2D region2D = new(-8.5f, 3.25f, 37, 29, 1.5f);
        float[] filled2D = noise.Create(region2D);

        for (int y = 0; y < region2D.Height; y++)
        {
            for (int x = 0; x < region2D.Width; x++)
            {
                float expected = noise.GetNoise(
                    region2D.OriginX + (x * region2D.Step),
                    region2D.OriginY + (y * region2D.Step));

                Assert.Equal(expected, filled2D[x + (y * region2D.Width)]);
            }
        }

        GridRegion3D region3D = new(2f, -6.5f, 0.25f, 17, 11, 5, 0.5f);
        float[] filled3D = noise.Create(region3D);

        for (int z = 0; z < region3D.Depth; z++)
        {
            for (int y = 0; y < region3D.Height; y++)
            {
                for (int x = 0; x < region3D.Width; x++)
                {
                    float expected = noise.GetNoise(
                        region3D.OriginX + (x * region3D.Step),
                        region3D.OriginY + (y * region3D.Step),
                        region3D.OriginZ + (z * region3D.Step));

                    int index = x + (region3D.Width * (y + (region3D.Height * z)));
                    Assert.Equal(expected, filled3D[index]);
                }
            }
        }
    }

    /// <summary>
    /// The row-tail path only runs when a row is not a whole number of vectors, so it needs widths
    /// chosen to hit every remainder.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(33)]
    public void RowTails_MatchPointSampling(int width)
    {
        NoiseGenerator noise = new(8) { NoiseType = NoiseType.Perlin, Frequency = 0.05f };
        GridRegion2D region = new(0.5f, 0.5f, width, 3);

        float[] filled = noise.Create(region, NoiseBackend.Simd);

        for (int y = 0; y < region.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(
                    noise.GetNoise(region.OriginX + x, region.OriginY + y),
                    filled[x + (y * width)]);
            }
        }
    }

    [Fact]
    public void VectorWidth_IsReported()
    {
        // Not an assertion about the hardware, just a record of what the run actually exercised.
        // A CI machine without vector support still runs every test above; it just proves less.
        Assert.True(Vector<float>.Count >= 1);
    }

    private static void AssertIdentical(float[] expected, float[] actual, string because)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (int index = 0; index < expected.Length; index++)
        {
            if (!expected[index].Equals(actual[index]))
            {
                Assert.Fail(
                    $"{because}: sample {index} differs. "
                    + $"Expected {expected[index]:R}, got {actual[index]:R}. "
                    + $"Vector<float>.Count = {Vector<float>.Count}.");
            }
        }
    }
}
