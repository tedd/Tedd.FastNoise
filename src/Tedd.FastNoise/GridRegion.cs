using System;

namespace Tedd.FastNoise;

/// <summary>
/// A rectangle of sample points: where to start, how many samples, and how far apart in world units.
/// </summary>
/// <param name="OriginX">World X of the first sample.</param>
/// <param name="OriginY">World Y of the first sample.</param>
/// <param name="Width">Sample count along X.</param>
/// <param name="Height">Sample count along Y.</param>
/// <param name="Step">
/// World-space distance between adjacent samples. This is the zoom control: 1 samples every block,
/// 64 samples every 64th block for a distant view. See <see cref="LodPolicy"/> for what the
/// generator does with it beyond spacing the samples out.
/// </param>
/// <remarks>
/// Results are written X-fastest: <c>destination[x + y * Width]</c>.
/// </remarks>
public readonly record struct GridRegion2D(float OriginX, float OriginY, int Width, int Height, float Step = 1f)
{
    /// <summary>Total samples the region covers.</summary>
    public int SampleCount => Width * Height;

    /// <summary>Throws if the region is degenerate or the destination is too small.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive, or the step is not finite and positive.</exception>
    /// <exception cref="ArgumentException">The destination cannot hold <see cref="SampleCount"/> values.</exception>
    public void Validate(int destinationLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);

        if (!float.IsFinite(Step) || Step <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Step), Step, "Step must be finite and greater than zero.");
        }

        if (destinationLength < SampleCount)
        {
            throw new ArgumentException(
                $"Destination holds {destinationLength} values but the region needs {SampleCount}.",
                nameof(destinationLength));
        }
    }
}

/// <summary>
/// A box of sample points: where to start, how many samples per axis, and how far apart in world units.
/// </summary>
/// <param name="OriginX">World X of the first sample.</param>
/// <param name="OriginY">World Y of the first sample.</param>
/// <param name="OriginZ">World Z of the first sample.</param>
/// <param name="Width">Sample count along X.</param>
/// <param name="Height">Sample count along Y.</param>
/// <param name="Depth">Sample count along Z.</param>
/// <param name="Step">
/// World-space distance between adjacent samples on every axis. See <see cref="LodPolicy"/>.
/// </param>
/// <remarks>
/// Results are written X-fastest, then Y, then Z: <c>destination[x + Width * (y + Height * z)]</c>.
/// Which world axis you call Y is up to you; nothing in the generator assumes an up direction.
/// </remarks>
public readonly record struct GridRegion3D(
    float OriginX,
    float OriginY,
    float OriginZ,
    int Width,
    int Height,
    int Depth,
    float Step = 1f)
{
    /// <summary>Total samples the region covers.</summary>
    public int SampleCount => Width * Height * Depth;

    /// <summary>
    /// A cube-shaped region covering one chunk of a uniformly chunked world.
    /// </summary>
    /// <param name="chunkX">Chunk index along X.</param>
    /// <param name="chunkY">Chunk index along Y.</param>
    /// <param name="chunkZ">Chunk index along Z.</param>
    /// <param name="chunkSize">Edge length of a chunk, in samples.</param>
    /// <param name="step">World units per sample. Leave at 1 for full detail.</param>
    public static GridRegion3D Chunk(int chunkX, int chunkY, int chunkZ, int chunkSize, float step = 1f)
        => new(
            chunkX * chunkSize * step,
            chunkY * chunkSize * step,
            chunkZ * chunkSize * step,
            chunkSize,
            chunkSize,
            chunkSize,
            step);

    /// <summary>Throws if the region is degenerate or the destination is too small.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive, or the step is not finite and positive.</exception>
    /// <exception cref="ArgumentException">The destination cannot hold <see cref="SampleCount"/> values.</exception>
    public void Validate(int destinationLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Depth);

        if (!float.IsFinite(Step) || Step <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Step), Step, "Step must be finite and greater than zero.");
        }

        if (destinationLength < SampleCount)
        {
            throw new ArgumentException(
                $"Destination holds {destinationLength} values but the region needs {SampleCount}.",
                nameof(destinationLength));
        }
    }
}
