using System;
using System.Runtime.CompilerServices;
using Tedd.FastNoise.Internal.Kernels;

namespace Tedd.FastNoise.Internal;

/// <summary>One layer, with its level-of-detail budget already resolved for the fill in progress.</summary>
internal readonly struct ResolvedLayer
{
    /// <summary>Algorithm settings.</summary>
    public required KernelConfig Kernel { get; init; }

    /// <summary>Octave settings at full detail.</summary>
    public required FractalConfig Fractal { get; init; }

    /// <summary>Seed of the layer's first octave.</summary>
    public required int Seed { get; init; }

    /// <summary>Cycles per world unit for the layer's first octave.</summary>
    public required float Frequency { get; init; }

    /// <summary>How this layer folds into the accumulator.</summary>
    public required LayerBlend Blend { get; init; }

    /// <summary>Multiplier applied before blending.</summary>
    public required float Amplitude { get; init; }

    /// <summary>Constant added before blending.</summary>
    public required float Offset { get; init; }

    /// <summary>Interpolation weight for <see cref="LayerBlend.Lerp"/>.</summary>
    public required float BlendFactor { get; init; }

    /// <summary>Octaves to run for this fill, after level-of-detail culling.</summary>
    public required int Octaves { get; init; }

    /// <summary>Amplitude multiplier for the final octave, in (0, 1].</summary>
    public required float LastOctaveFade { get; init; }
}

/// <summary>
/// The fused multi-layer fill: every layer of a stack evaluated for one set of coordinates before
/// anything is written.
/// </summary>
/// <remarks>
/// <para>
/// The obvious way to combine layers is to fill a buffer per layer and then walk the buffers
/// combining them. It is also the slow way. Six layers over a 16x16x256 column means six full
/// passes writing 256 KB each, then a seventh pass reading all of it back -- roughly 1.8 MB of
/// traffic to produce 256 KB of output, none of which stays in cache.
/// </para>
/// <para>
/// This loop instead holds the accumulator in a vector register and runs every layer against the
/// coordinates currently in flight. Total traffic is one write per output value. The layer loop
/// costs a predictable branch per layer per vector step, against kernels that are hundreds of
/// operations each, so it does not register.
/// </para>
/// <para>
/// This is the whole reason <see cref="NoiseStack.Compile"/> exists: it flattens the layer objects
/// into the flat array this loop walks, with level of detail already resolved, so nothing inside
/// the loop has to think.
/// </para>
/// </remarks>
internal static class StackFill
{
    /// <summary>Fills rows of a 2D region with the whole stack.</summary>
    public static void Rows2<TOps, TF, TI>(
        ResolvedLayer[] layers,
        in GridRegion2D region,
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
                TF baseX = TOps.Ramp(region.OriginX + (column * step), step);
                TF baseY = TOps.F(worldY);

                TOps.Store(Evaluate2<TOps, TF, TI>(layers, baseX, baseY), destination.Slice(rowStart + column, lanes));
            }

            for (; column < width; column++)
            {
                destination[rowStart + column] = Evaluate2<ScalarOps, float, int>(
                    layers, region.OriginX + (column * step), worldY);
            }
        }
    }

    /// <summary>Fills rows of a 3D region with the whole stack.</summary>
    public static void Rows3<TOps, TF, TI>(
        ResolvedLayer[] layers,
        in GridRegion3D region,
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
                TF baseX = TOps.Ramp(region.OriginX + (column * step), step);
                TF baseY = TOps.F(worldY);
                TF baseZ = TOps.F(worldZ);

                TOps.Store(
                    Evaluate3<TOps, TF, TI>(layers, baseX, baseY, baseZ),
                    destination.Slice(rowStart + column, lanes));
            }

            for (; column < width; column++)
            {
                destination[rowStart + column] = Evaluate3<ScalarOps, float, int>(
                    layers, region.OriginX + (column * step), worldY, worldZ);
            }
        }
    }

    /// <summary>Runs every layer against one set of 2D coordinates and returns the blended result.</summary>
    public static TF Evaluate2<TOps, TF, TI>(ResolvedLayer[] layers, TF worldX, TF worldY)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TF accumulator = TOps.F(0f);

        for (int index = 0; index < layers.Length; index++)
        {
            ref readonly ResolvedLayer layer = ref layers[index];

            // Each layer reads the original coordinates; frequency and skew are per layer.
            TF x = worldX;
            TF y = worldY;
            NoisePipeline.Transform2<TOps, TF, TI>(layer.Kernel.NoiseType, layer.Frequency, ref x, ref y);

            TF value = FractalKernel.Fractal2<TOps, TF, TI>(
                layer.Kernel, layer.Fractal, layer.Seed, x, y, layer.Octaves, layer.LastOctaveFade);

            value = TOps.Add(TOps.Mul(value, TOps.F(layer.Amplitude)), TOps.F(layer.Offset));

            accumulator = index == 0
                ? value
                : Blend<TOps, TF, TI>(layer.Blend, accumulator, value, layer.BlendFactor);
        }

        return accumulator;
    }

    /// <summary>Runs every layer against one set of 3D coordinates and returns the blended result.</summary>
    public static TF Evaluate3<TOps, TF, TI>(ResolvedLayer[] layers, TF worldX, TF worldY, TF worldZ)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TF accumulator = TOps.F(0f);

        for (int index = 0; index < layers.Length; index++)
        {
            ref readonly ResolvedLayer layer = ref layers[index];

            TF x = worldX;
            TF y = worldY;
            TF z = worldZ;
            NoisePipeline.Transform3<TOps, TF, TI>(layer.Kernel.Transform3D, layer.Frequency, ref x, ref y, ref z);

            TF value = FractalKernel.Fractal3<TOps, TF, TI>(
                layer.Kernel, layer.Fractal, layer.Seed, x, y, z, layer.Octaves, layer.LastOctaveFade);

            value = TOps.Add(TOps.Mul(value, TOps.F(layer.Amplitude)), TOps.F(layer.Offset));

            accumulator = index == 0
                ? value
                : Blend<TOps, TF, TI>(layer.Blend, accumulator, value, layer.BlendFactor);
        }

        return accumulator;
    }

    /// <summary>Folds one layer's value into the accumulator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TF Blend<TOps, TF, TI>(LayerBlend blend, TF accumulator, TF value, float factor)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => blend switch
        {
            LayerBlend.Add => TOps.Add(accumulator, value),
            LayerBlend.Subtract => TOps.Sub(accumulator, value),
            LayerBlend.Multiply => TOps.Mul(accumulator, value),
            LayerBlend.Min => TOps.Min(accumulator, value),
            LayerBlend.Max => TOps.Max(accumulator, value),
            LayerBlend.Replace => value,
            LayerBlend.Lerp => NoiseMath.Lerp<TOps, TF, TI>(accumulator, value, TOps.F(factor)),
            _ => TOps.Add(accumulator, value),
        };
}
