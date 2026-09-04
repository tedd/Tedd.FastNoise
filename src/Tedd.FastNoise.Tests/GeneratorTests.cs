using System;
using System.Linq;
using System.Threading.Tasks;

namespace Tedd.FastNoise.Tests;

/// <summary>Argument handling, determinism, output range, and the accelerator fallback.</summary>
public class GeneratorTests
{
    [Fact]
    public void Fill_RejectsUndersizedDestination()
    {
        NoiseGenerator noise = new();
        float[] tooSmall = new float[10];

        Assert.Throws<ArgumentException>(() => noise.Fill(tooSmall, new GridRegion2D(0, 0, 8, 8)));
        Assert.Throws<ArgumentException>(() => noise.Fill(tooSmall, new GridRegion3D(0, 0, 0, 4, 4, 4)));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    public void Fill_RejectsDegenerateRegions(int width, int height)
    {
        NoiseGenerator noise = new();
        float[] destination = new float[1024];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => noise.Fill(destination, new GridRegion2D(0, 0, width, height)));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Fill_RejectsInvalidStep(float step)
    {
        NoiseGenerator noise = new();
        float[] destination = new float[1024];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => noise.Fill(destination, new GridRegion2D(0, 0, 8, 8, step)));
    }

    [Fact]
    public void Octaves_RejectsZeroAndBelow()
    {
        NoiseGenerator noise = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => noise.Octaves = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => noise.Octaves = -3);
    }

    [Theory]
    [MemberData(nameof(NoiseCases.AllNoiseTypes), MemberType = typeof(NoiseCases))]
    public void SameSeed_SameOutput(NoiseType noiseType)
    {
        GridRegion2D region = new(123.5f, -456.25f, 32, 32);

        float[] first = new NoiseGenerator(4242) { NoiseType = noiseType }.Create(region);
        float[] second = new NoiseGenerator(4242) { NoiseType = noiseType }.Create(region);

        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(NoiseCases.AllNoiseTypes), MemberType = typeof(NoiseCases))]
    public void DifferentSeed_DifferentOutput(NoiseType noiseType)
    {
        GridRegion2D region = new(0f, 0f, 32, 32);

        float[] first = new NoiseGenerator(1) { NoiseType = noiseType }.Create(region);
        float[] second = new NoiseGenerator(2) { NoiseType = noiseType }.Create(region);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// Every kernel claims to return roughly [-1, 1]. Callers map that onto world height, so a
    /// kernel that quietly returns 3.0 puts terrain through the sky.
    /// </summary>
    [Theory]
    [MemberData(nameof(NoiseCases.NoiseAndFractalTypes), MemberType = typeof(NoiseCases))]
    public void Output_StaysWithinTheAdvertisedRange(NoiseType noiseType, FractalType fractalType)
    {
        NoiseGenerator noise = new(17)
        {
            NoiseType = noiseType,
            FractalType = fractalType,
            Octaves = 5,
            Frequency = 0.02f,
        };

        float[] values = noise.Create(new GridRegion3D(-500f, -500f, -500f, 48, 48, 24, 1.7f));

        float minimum = values.Min();
        float maximum = values.Max();

        // A little headroom: gradient noise is not analytically bounded at exactly 1.
        Assert.InRange(minimum, -1.2f, 1.2f);
        Assert.InRange(maximum, -1.2f, 1.2f);

        // And it must actually use the range rather than sitting near zero.
        Assert.True(maximum - minimum > 0.2f, $"{noiseType}/{fractalType} spanned only {maximum - minimum:0.###}.");
        Assert.All(values, static value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void Fill_IsSafeFromManyThreadsAtOnce()
    {
        NoiseGenerator noise = new(5)
        {
            NoiseType = NoiseType.OpenSimplex2,
            FractalType = FractalType.FBm,
            Octaves = 4,
        };

        GridRegion2D region = new(0f, 0f, 64, 64);
        float[] expected = noise.Create(region);

        Parallel.For(0, 64, _ =>
        {
            float[] actual = noise.Create(region);
            Assert.Equal(expected, actual);
        });
    }

    [Fact]
    public void GpuBackend_FallsBackWhenNothingIsRegistered()
    {
        Assert.Null(NoiseAccelerator.Current);

        NoiseGenerator noise = new(99);
        GridRegion2D region = new(0f, 0f, 64, 64);

        Assert.Equal(noise.Create(region, NoiseBackend.Scalar), noise.Create(region, NoiseBackend.Gpu));
    }

    [Fact]
    public void GpuBackend_UsesAnAcceleratorThatAcceptsTheWork()
    {
        RecordingAccelerator accelerator = new(accept: true);
        NoiseAccelerator.Current = accelerator;

        try
        {
            NoiseGenerator noise = new(99);
            float[] result = noise.Create(new GridRegion2D(0f, 0f, 64, 64), NoiseBackend.Gpu);

            Assert.True(accelerator.Called);
            Assert.All(result, static value => Assert.Equal(0.5f, value));
        }
        finally
        {
            NoiseAccelerator.Current = null;
        }
    }

    [Fact]
    public void GpuBackend_FallsBackWhenTheAcceleratorDeclines()
    {
        RecordingAccelerator accelerator = new(accept: false);
        NoiseAccelerator.Current = accelerator;

        try
        {
            NoiseGenerator noise = new(99);
            GridRegion2D region = new(0f, 0f, 64, 64);

            float[] result = noise.Create(region, NoiseBackend.Gpu);

            Assert.True(accelerator.Called);
            Assert.Equal(noise.Create(region, NoiseBackend.Scalar), result);
        }
        finally
        {
            NoiseAccelerator.Current = null;
        }
    }

    [Fact]
    public void GpuBackend_IsNotAskedAboutSmallFills()
    {
        RecordingAccelerator accelerator = new(accept: true) { Minimum = 1_000_000 };
        NoiseAccelerator.Current = accelerator;

        try
        {
            new NoiseGenerator(99).Create(new GridRegion2D(0f, 0f, 4, 4), NoiseBackend.Gpu);
            Assert.False(accelerator.Called);
        }
        finally
        {
            NoiseAccelerator.Current = null;
        }
    }

    /// <summary>A stand-in accelerator that records whether it was asked and writes a recognisable value.</summary>
    private sealed class RecordingAccelerator(bool accept) : INoiseAccelerator
    {
        public bool Called { get; private set; }

        public int Minimum { get; init; } = 1;

        public bool IsAvailable => true;

        public int MinimumSampleCount => Minimum;

        public bool TryFill2D(Span<float> destination, in NoiseFillRequest2D request)
        {
            Called = true;
            if (!accept)
            {
                return false;
            }

            destination[..request.Region.SampleCount].Fill(0.5f);
            return true;
        }

        public bool TryFill3D(Span<float> destination, in NoiseFillRequest3D request)
        {
            Called = true;
            if (!accept)
            {
                return false;
            }

            destination[..request.Region.SampleCount].Fill(0.5f);
            return true;
        }
    }
}
