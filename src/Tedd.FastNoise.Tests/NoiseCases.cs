using System.Collections.Generic;
using Tedd.FastNoise.Tests.Reference;

namespace Tedd.FastNoise.Tests;

/// <summary>
/// Shared configuration matrices, and the machinery for configuring a
/// <see cref="NoiseGenerator"/> and the reference implementation identically.
/// </summary>
internal static class NoiseCases
{
    /// <summary>Every algorithm.</summary>
    public static IEnumerable<object[]> AllNoiseTypes()
    {
        foreach (NoiseType type in System.Enum.GetValues<NoiseType>())
        {
            yield return [type];
        }
    }

    /// <summary>Every algorithm crossed with every way of combining octaves.</summary>
    public static IEnumerable<object[]> NoiseAndFractalTypes()
    {
        foreach (NoiseType noise in System.Enum.GetValues<NoiseType>())
        {
            foreach (FractalType fractal in new[] { FractalType.None, FractalType.FBm, FractalType.Ridged, FractalType.PingPong })
            {
                yield return [noise, fractal];
            }
        }
    }

    /// <summary>Every cellular distance metric crossed with every output selection.</summary>
    public static IEnumerable<object[]> CellularVariants()
    {
        foreach (CellularDistanceFunction distance in System.Enum.GetValues<CellularDistanceFunction>())
        {
            foreach (CellularReturnType returns in System.Enum.GetValues<CellularReturnType>())
            {
                yield return [distance, returns];
            }
        }
    }

    /// <summary>Every 3D domain rotation.</summary>
    public static IEnumerable<object[]> Rotations()
    {
        foreach (RotationType3D rotation in System.Enum.GetValues<RotationType3D>())
        {
            yield return [rotation];
        }
    }

    /// <summary>A generator and a reference instance configured identically.</summary>
    /// <param name="seed">Seed for both.</param>
    /// <param name="noiseType">Algorithm for both.</param>
    /// <param name="fractalType">Octave combination for both.</param>
    /// <param name="octaves">Octave count for both.</param>
    /// <param name="frequency">Base frequency for both.</param>
    /// <param name="weightedStrength">Per-octave amplitude bias for both.</param>
    /// <param name="rotation">3D domain rotation for both.</param>
    /// <param name="cellularDistance">Cellular distance metric for both.</param>
    /// <param name="cellularReturn">Cellular output selection for both.</param>
    /// <param name="cellularJitter">Cellular feature-point displacement for both.</param>
    public static (NoiseGenerator Subject, FastNoiseLiteReference Oracle) Pair(
        int seed = 1337,
        NoiseType noiseType = NoiseType.OpenSimplex2,
        FractalType fractalType = FractalType.None,
        int octaves = 3,
        float frequency = 0.01f,
        float weightedStrength = 0f,
        RotationType3D rotation = RotationType3D.None,
        CellularDistanceFunction cellularDistance = CellularDistanceFunction.EuclideanSq,
        CellularReturnType cellularReturn = CellularReturnType.Distance,
        float cellularJitter = 1f)
    {
        NoiseGenerator subject = new(seed)
        {
            NoiseType = noiseType,
            FractalType = fractalType,
            Octaves = octaves,
            Frequency = frequency,
            WeightedStrength = weightedStrength,
            RotationType3D = rotation,
            CellularDistanceFunction = cellularDistance,
            CellularReturnType = cellularReturn,
            CellularJitter = cellularJitter,
        };

        FastNoiseLiteReference oracle = new(seed);
        oracle.SetNoiseType((FastNoiseLiteReference.NoiseType)noiseType);
        oracle.SetFractalType((FastNoiseLiteReference.FractalType)fractalType);
        oracle.SetFractalOctaves(octaves);
        oracle.SetFrequency(frequency);
        oracle.SetFractalWeightedStrength(weightedStrength);
        oracle.SetRotationType3D((FastNoiseLiteReference.RotationType3D)rotation);
        oracle.SetCellularDistanceFunction((FastNoiseLiteReference.CellularDistanceFunction)cellularDistance);
        oracle.SetCellularReturnType((FastNoiseLiteReference.CellularReturnType)cellularReturn);
        oracle.SetCellularJitter(cellularJitter);

        return (subject, oracle);
    }
}
