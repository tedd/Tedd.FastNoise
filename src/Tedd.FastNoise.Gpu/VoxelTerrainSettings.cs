// Defines the density-to-cell mapping and the Forcecraft brick payload capacity.
// Palette entry zero is air; entry one is an opaque, full-height terrain material.
using System;
using System.Numerics;

namespace Tedd.FastNoise.Gpu;

/// <summary>A solid cell satisfies noise * Amplitude + Height - worldY * VerticalScale &gt; 0.</summary>
/// <remarks>Use VerticalScale = 0 for a thresholded 3D density field. Lighting is a constant,
/// not an occlusion calculation. Material colors are packed RGBA8, least significant byte red.</remarks>
public readonly record struct VoxelTerrainSettings(
    float Height = 0, float Amplitude = 32, float VerticalScale = 1,
    uint Color = 0xff608050, byte Sky = 255)
{
    /// <summary>Creates the conventional height-biased 3D terrain settings.</summary>
    public VoxelTerrainSettings() : this(0, 32, 1, 0xff608050, 255) { }

    /// <summary>Worst-case output capacity, including header, brick directory and two six-face materials.</summary>
    public static ulong RequiredBytes(int side)
    {
        if (side is < 8 or > 256 || !BitOperations.IsPow2((uint)side))
            throw new ArgumentOutOfRangeException(nameof(side), "Side must be a power of two from 8 through 256.");
        return (ulong)(4 + (side / 4) * (side / 4) * (side / 4) + side * side * side * 2 + 48) * 4;
    }

    internal void Validate()
    {
        if (!float.IsFinite(Height) || !float.IsFinite(Amplitude) || !float.IsFinite(VerticalScale))
            throw new ArgumentException("Terrain coefficients must be finite.");
        if ((Color >> 24) != 255)
            throw new ArgumentException("The terrain material must be opaque.", nameof(Color));
    }
}
