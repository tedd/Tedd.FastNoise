using System;
using Tedd.FastNoise.Tests.Reference;

namespace Tedd.FastNoise.Tests;

/// <summary>
/// Porting fidelity: every kernel is compared against an unmodified FastNoiseLite for exact equality.
/// </summary>
/// <remarks>
/// <para>
/// "Bit for bit" is the assertion, not "close enough". The kernels here were rewritten to
/// vectorise -- branches turned into masks, loops unrolled, corner selection restructured -- and
/// every one of those rewrites is a chance to change a value by an ULP and never notice. Comparing
/// against an unmodified copy of the reference is what makes those rewrites safe to make. It found
/// two real bugs during the port, both float association differences that a tolerance-based test
/// would have waved through.
/// </para>
/// <para>
/// These tests describe the port as it stands, not a permanent contract. As this library diverges
/// from upstream -- better quality, new algorithms, features FastNoiseLite has no reason to carry --
/// the test for a changed algorithm should be retired along with it, deliberately and in the same
/// commit. What must not happen is a kernel drifting quietly while its oracle test still passes
/// because someone loosened it to a tolerance.
/// </para>
/// </remarks>
public class CompatibilityTests
{
    private const int SampleCount = 20_000;

    [Theory]
    [MemberData(nameof(NoiseCases.NoiseAndFractalTypes), MemberType = typeof(NoiseCases))]
    public void Noise2D_MatchesReferenceExactly(NoiseType noiseType, FractalType fractalType)
    {
        (NoiseGenerator subject, FastNoiseLiteReference oracle) = NoiseCases.Pair(
            noiseType: noiseType, fractalType: fractalType, octaves: 4);

        Random random = new(Seed: 5150);

        for (int i = 0; i < SampleCount; i++)
        {
            float x = ((float)random.NextDouble() - 0.5f) * 4000f;
            float y = ((float)random.NextDouble() - 0.5f) * 4000f;

            Assert.Equal(oracle.GetNoise(x, y), subject.GetNoise(x, y));
        }
    }

    [Theory]
    [MemberData(nameof(NoiseCases.NoiseAndFractalTypes), MemberType = typeof(NoiseCases))]
    public void Noise3D_MatchesReferenceExactly(NoiseType noiseType, FractalType fractalType)
    {
        (NoiseGenerator subject, FastNoiseLiteReference oracle) = NoiseCases.Pair(
            noiseType: noiseType, fractalType: fractalType, octaves: 4);

        Random random = new(Seed: 5151);

        for (int i = 0; i < SampleCount; i++)
        {
            float x = ((float)random.NextDouble() - 0.5f) * 4000f;
            float y = ((float)random.NextDouble() - 0.5f) * 4000f;
            float z = ((float)random.NextDouble() - 0.5f) * 4000f;

            Assert.Equal(oracle.GetNoise(x, y, z), subject.GetNoise(x, y, z));
        }
    }

    [Theory]
    [MemberData(nameof(NoiseCases.CellularVariants), MemberType = typeof(NoiseCases))]
    public void Cellular_MatchesReferenceExactly(CellularDistanceFunction distance, CellularReturnType returns)
    {
        (NoiseGenerator subject, FastNoiseLiteReference oracle) = NoiseCases.Pair(
            noiseType: NoiseType.Cellular,
            cellularDistance: distance,
            cellularReturn: returns,
            cellularJitter: 0.8f);

        Random random = new(Seed: 909);

        for (int i = 0; i < 5_000; i++)
        {
            float x = ((float)random.NextDouble() - 0.5f) * 2000f;
            float y = ((float)random.NextDouble() - 0.5f) * 2000f;
            float z = ((float)random.NextDouble() - 0.5f) * 2000f;

            Assert.Equal(oracle.GetNoise(x, y), subject.GetNoise(x, y));
            Assert.Equal(oracle.GetNoise(x, y, z), subject.GetNoise(x, y, z));
        }
    }

    [Theory]
    [MemberData(nameof(NoiseCases.Rotations), MemberType = typeof(NoiseCases))]
    public void Rotation3D_MatchesReferenceExactly(RotationType3D rotation)
    {
        foreach (NoiseType noiseType in Enum.GetValues<NoiseType>())
        {
            (NoiseGenerator subject, FastNoiseLiteReference oracle) = NoiseCases.Pair(
                noiseType: noiseType, rotation: rotation);

            Random random = new(Seed: 4242);

            for (int i = 0; i < 2_000; i++)
            {
                float x = ((float)random.NextDouble() - 0.5f) * 1000f;
                float y = ((float)random.NextDouble() - 0.5f) * 1000f;
                float z = ((float)random.NextDouble() - 0.5f) * 1000f;

                Assert.Equal(oracle.GetNoise(x, y, z), subject.GetNoise(x, y, z));
            }
        }
    }

    /// <summary>
    /// Weighted strength makes octaves depend on each other, which is the one thing that would
    /// silently break if the octave loop were reordered or flattened.
    /// </summary>
    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void WeightedStrength_MatchesReferenceExactly(float weightedStrength)
    {
        foreach (FractalType fractalType in new[] { FractalType.FBm, FractalType.Ridged, FractalType.PingPong })
        {
            (NoiseGenerator subject, FastNoiseLiteReference oracle) = NoiseCases.Pair(
                noiseType: NoiseType.Perlin,
                fractalType: fractalType,
                octaves: 6,
                weightedStrength: weightedStrength);

            Random random = new(Seed: 77);

            for (int i = 0; i < 3_000; i++)
            {
                float x = ((float)random.NextDouble() - 0.5f) * 800f;
                float y = ((float)random.NextDouble() - 0.5f) * 800f;
                float z = ((float)random.NextDouble() - 0.5f) * 800f;

                Assert.Equal(oracle.GetNoise(x, y), subject.GetNoise(x, y));
                Assert.Equal(oracle.GetNoise(x, y, z), subject.GetNoise(x, y, z));
            }
        }
    }

    /// <summary>
    /// Exercises the lattice boundary specifically. The reference's floor rounds -1.0 down to -2,
    /// so the fast path has to do the same or results diverge on a measure-zero set of inputs that
    /// a uniform random sweep would essentially never hit.
    /// </summary>
    [Theory]
    [MemberData(nameof(NoiseCases.AllNoiseTypes), MemberType = typeof(NoiseCases))]
    public void ExactLatticeCoordinates_MatchReferenceExactly(NoiseType noiseType)
    {
        // Frequency 1 puts world coordinates directly onto lattice coordinates.
        (NoiseGenerator subject, FastNoiseLiteReference oracle) = NoiseCases.Pair(
            noiseType: noiseType, frequency: 1f);

        for (int x = -20; x <= 20; x++)
        {
            for (int y = -20; y <= 20; y++)
            {
                Assert.Equal(oracle.GetNoise(x, y), subject.GetNoise(x, y));
                Assert.Equal(oracle.GetNoise(x, y, -3f), subject.GetNoise(x, y, -3f));
                Assert.Equal(oracle.GetNoise(x + 0.5f, y - 0.5f), subject.GetNoise(x + 0.5f, y - 0.5f));
            }
        }
    }

    [Fact]
    public void DomainWarp_MatchesReferenceExactly()
    {
        foreach (DomainWarpType warpType in Enum.GetValues<DomainWarpType>())
        {
            foreach (FractalType fractalType in new[] { FractalType.None, FractalType.DomainWarpProgressive, FractalType.DomainWarpIndependent })
            {
                NoiseGenerator subject = new(1337)
                {
                    DomainWarpType = warpType,
                    DomainWarpAmplitude = 30f,
                    FractalType = fractalType,
                    Octaves = 3,
                    Frequency = 0.02f,
                };

                FastNoiseLiteReference oracle = new(1337);
                oracle.SetDomainWarpType((FastNoiseLiteReference.DomainWarpType)warpType);
                oracle.SetDomainWarpAmp(30f);
                oracle.SetFractalType((FastNoiseLiteReference.FractalType)fractalType);
                oracle.SetFractalOctaves(3);
                oracle.SetFrequency(0.02f);

                Random random = new(Seed: 31337);

                for (int i = 0; i < 2_000; i++)
                {
                    float x = ((float)random.NextDouble() - 0.5f) * 1000f;
                    float y = ((float)random.NextDouble() - 0.5f) * 1000f;
                    float z = ((float)random.NextDouble() - 0.5f) * 1000f;

                    float subjectX = x, subjectY = y, subjectZ = z;
                    float oracleX = x, oracleY = y, oracleZ = z;

                    subject.DomainWarp(ref subjectX, ref subjectY);
                    oracle.DomainWarp(ref oracleX, ref oracleY);
                    Assert.Equal(oracleX, subjectX);
                    Assert.Equal(oracleY, subjectY);

                    subjectX = x; subjectY = y; subjectZ = z;
                    oracleX = x; oracleY = y; oracleZ = z;

                    subject.DomainWarp(ref subjectX, ref subjectY, ref subjectZ);
                    oracle.DomainWarp(ref oracleX, ref oracleY, ref oracleZ);
                    Assert.Equal(oracleX, subjectX);
                    Assert.Equal(oracleY, subjectY);
                    Assert.Equal(oracleZ, subjectZ);
                }
            }
        }
    }
}
