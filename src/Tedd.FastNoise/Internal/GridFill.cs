using System;
using System.Runtime.CompilerServices;
using Tedd.FastNoise.Internal.Kernels;

namespace Tedd.FastNoise.Internal;

/// <summary>
/// Bulk sampling over a grid or volume: the part of the library that actually goes fast.
/// </summary>
/// <remarks>
/// <para>
/// Rows run along X, and X is the axis whose coordinates differ within a SIMD register. That is
/// the whole trick: <c>Ramp</c> builds <c>origin, origin+step, origin+2*step, ...</c> in one
/// instruction, the other axes are broadcast constants, and the kernel then runs unchanged with
/// every lane doing useful work. No gathers, no shuffling, no transposes.
/// </para>
/// <para>
/// Work is addressed by a flat row index so that 2D and 3D partition identically: row <c>r</c> of a
/// volume is <c>(y, z) = (r % Height, r / Height)</c>. A parallel fill hands contiguous row ranges
/// to workers, which keeps each worker writing to its own cache lines.
/// </para>
/// </remarks>
internal static class GridFill
{
    /// <summary>Fills <paramref name="rowCount"/> rows of a 2D region, starting at <paramref name="firstRow"/>.</summary>
    /// <remarks>
    /// Instantiated once per lane width. <c>ScalarOps</c> gives the portable path with a lane count
    /// of one, which makes the tail loop below dead code there rather than a special case.
    /// </remarks>
    public static void Rows2<TOps, TF, TI>(
        in KernelConfig kernel,
        in FractalConfig fractal,
        int seed,
        float frequency,
        in GridRegion2D region,
        int octaves,
        float lastOctaveFade,
        Span<float> destination,
        int firstRow,
        int rowCount)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        int lanes = TOps.Count;
        int width = region.Width;
        float step = region.Step;

        for (int row = firstRow; row < firstRow + rowCount; row++)
        {
            float worldY = region.OriginY + (row * step);
            int rowStart = row * width;
            int column = 0;

            for (; column + lanes <= width; column += lanes)
            {
                TF x = TOps.Ramp(region.OriginX + (column * step), step);
                TF y = TOps.F(worldY);

                NoisePipeline.Transform2<TOps, TF, TI>(kernel.NoiseType, frequency, ref x, ref y);
                TF value = FractalKernel.Fractal2<TOps, TF, TI>(kernel, fractal, seed, x, y, octaves, lastOctaveFade);

                TOps.Store(value, destination, rowStart + column);
            }

            // Rows whose width is not a whole number of vectors finish scalar.
            for (; column < width; column++)
            {
                destination[rowStart + column] = Point2(
                    kernel, fractal, seed, frequency,
                    region.OriginX + (column * step), worldY, octaves, lastOctaveFade);
            }
        }
    }

    /// <summary>Fills <paramref name="rowCount"/> rows of a 3D region, starting at <paramref name="firstRow"/>.</summary>
    public static void Rows3<TOps, TF, TI>(
        in KernelConfig kernel,
        in FractalConfig fractal,
        int seed,
        float frequency,
        in GridRegion3D region,
        int octaves,
        float lastOctaveFade,
        Span<float> destination,
        int firstRow,
        int rowCount)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        int lanes = TOps.Count;
        int width = region.Width;
        int height = region.Height;
        float step = region.Step;

        for (int row = firstRow; row < firstRow + rowCount; row++)
        {
            int y = row % height;
            int z = row / height;

            float worldY = region.OriginY + (y * step);
            float worldZ = region.OriginZ + (z * step);
            int rowStart = row * width;
            int column = 0;

            for (; column + lanes <= width; column += lanes)
            {
                TF vx = TOps.Ramp(region.OriginX + (column * step), step);
                TF vy = TOps.F(worldY);
                TF vz = TOps.F(worldZ);

                NoisePipeline.Transform3<TOps, TF, TI>(kernel.Transform3D, frequency, ref vx, ref vy, ref vz);
                TF value = FractalKernel.Fractal3<TOps, TF, TI>(kernel, fractal, seed, vx, vy, vz, octaves, lastOctaveFade);

                TOps.Store(value, destination, rowStart + column);
            }

            for (; column < width; column++)
            {
                destination[rowStart + column] = Point3(
                    kernel, fractal, seed, frequency,
                    region.OriginX + (column * step), worldY, worldZ, octaves, lastOctaveFade);
            }
        }
    }

    /// <summary>
    /// Fills rows of a 2D region by calling the reference implementation once per sample.
    /// </summary>
    /// <remarks>
    /// The path for OpenSimplex2S, which has no wide kernel. Still worth running in parallel, and
    /// still benefits from the row partitioning above -- just not from vector registers.
    /// </remarks>
    public static void ReferenceRows2(
        FastNoiseLiteCore core,
        in GridRegion2D region,
        Span<float> destination,
        int firstRow,
        int rowCount)
    {
        int width = region.Width;
        float step = region.Step;

        for (int row = firstRow; row < firstRow + rowCount; row++)
        {
            float worldY = region.OriginY + (row * step);
            int rowStart = row * width;

            for (int column = 0; column < width; column++)
            {
                destination[rowStart + column] = core.GetNoise(region.OriginX + (column * step), worldY);
            }
        }
    }

    /// <summary>Fills rows of a 3D region by calling the reference implementation once per sample.</summary>
    public static void ReferenceRows3(
        FastNoiseLiteCore core,
        in GridRegion3D region,
        Span<float> destination,
        int firstRow,
        int rowCount)
    {
        int width = region.Width;
        int height = region.Height;
        float step = region.Step;

        for (int row = firstRow; row < firstRow + rowCount; row++)
        {
            int y = row % height;
            int z = row / height;

            float worldY = region.OriginY + (y * step);
            float worldZ = region.OriginZ + (z * step);
            int rowStart = row * width;

            for (int column = 0; column < width; column++)
            {
                destination[rowStart + column] = core.GetNoise(region.OriginX + (column * step), worldY, worldZ);
            }
        }
    }

    /// <summary>Scalar single sample, used for row tails.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Point2(
        in KernelConfig kernel, in FractalConfig fractal, int seed, float frequency,
        float x, float y, int octaves, float lastOctaveFade)
    {
        NoisePipeline.Transform2<ScalarOps, float, int>(kernel.NoiseType, frequency, ref x, ref y);
        return FractalKernel.Fractal2<ScalarOps, float, int>(kernel, fractal, seed, x, y, octaves, lastOctaveFade);
    }

    /// <summary>Scalar single sample, used for row tails.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Point3(
        in KernelConfig kernel, in FractalConfig fractal, int seed, float frequency,
        float x, float y, float z, int octaves, float lastOctaveFade)
    {
        NoisePipeline.Transform3<ScalarOps, float, int>(kernel.Transform3D, frequency, ref x, ref y, ref z);
        return FractalKernel.Fractal3<ScalarOps, float, int>(kernel, fractal, seed, x, y, z, octaves, lastOctaveFade);
    }
}
