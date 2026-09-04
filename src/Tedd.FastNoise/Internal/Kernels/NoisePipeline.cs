using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal.Kernels;

/// <summary>The 3D domain rotation to apply before sampling, resolved from noise type and rotation setting.</summary>
internal enum TransformType3D
{
    /// <summary>Sample the coordinates as given.</summary>
    None = 0,

    /// <summary>Rotate so that XY planes cut the lattice evenly.</summary>
    ImproveXYPlanes = 1,

    /// <summary>Rotate so that XZ planes cut the lattice evenly.</summary>
    ImproveXZPlanes = 2,

    /// <summary>The rotation OpenSimplex2 needs to place its lattice.</summary>
    DefaultOpenSimplex2 = 3,
}

/// <summary>Everything a kernel needs that is not a coordinate. Copied into fills, never mutated during one.</summary>
internal readonly struct KernelConfig
{
    /// <summary>Which algorithm to run.</summary>
    public required NoiseType NoiseType { get; init; }

    /// <summary>The 3D domain rotation, precomputed from noise type and user rotation setting.</summary>
    public required TransformType3D Transform3D { get; init; }

    /// <summary>Distance metric, ignored unless <see cref="NoiseType"/> is <see cref="NoiseType.Cellular"/>.</summary>
    public CellularDistanceFunction CellularDistance { get; init; }

    /// <summary>Output selection, ignored unless <see cref="NoiseType"/> is <see cref="NoiseType.Cellular"/>.</summary>
    public CellularReturnType CellularReturn { get; init; }

    /// <summary>Feature-point displacement multiplier, ignored unless <see cref="NoiseType"/> is <see cref="NoiseType.Cellular"/>.</summary>
    public float CellularJitter { get; init; }
}

/// <summary>
/// Ties the kernels together: coordinate transform, algorithm selection, fractal layering.
/// </summary>
/// <remarks>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md.
/// </remarks>
internal static class NoisePipeline
{
    private const float Sqrt3 = 1.7320508075688772935274463415059f;

    /// <summary>Skew factor taking the square lattice to the triangular one.</summary>
    private const float F2 = 0.5f * (Sqrt3 - 1);

    /// <summary>Rotation coefficient for the OpenSimplex2 3D lattice.</summary>
    private const float R3 = 2f / 3f;

    /// <summary>
    /// True when the algorithm has a lane-width-agnostic kernel and can therefore run wide.
    /// </summary>
    /// <remarks>
    /// OpenSimplex2S is the exception. Its corner selection is a rank comparison chain resolved
    /// before any arithmetic happens, and lanes in a vector will disagree about which branch to
    /// take, so a wide version would have to evaluate every arm of a wide tree per corner and
    /// select. That is a lot of speculative gradient work for an uncertain win, so bulk fills run
    /// the scalar reference per sample for this type instead. Still parallelised, just not wide.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasWideKernel(NoiseType type) => type != NoiseType.OpenSimplex2S;

    /// <summary>Applies frequency and any lattice skew to 2D coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Transform2<TOps, TF, TI>(NoiseType noiseType, float frequency, ref TF x, ref TF y)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TF f = TOps.F(frequency);
        x = TOps.Mul(x, f);
        y = TOps.Mul(y, f);

        if (noiseType is NoiseType.OpenSimplex2 or NoiseType.OpenSimplex2S)
        {
            TF t = TOps.Mul(TOps.Add(x, y), TOps.F(F2));
            x = TOps.Add(x, t);
            y = TOps.Add(y, t);
        }
    }

    /// <summary>Applies frequency and any lattice rotation to 3D coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Transform3<TOps, TF, TI>(TransformType3D transform, float frequency, ref TF x, ref TF y, ref TF z)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TF f = TOps.F(frequency);
        x = TOps.Mul(x, f);
        y = TOps.Mul(y, f);
        z = TOps.Mul(z, f);

        switch (transform)
        {
            case TransformType3D.ImproveXYPlanes:
            {
                TF xy = TOps.Add(x, y);
                TF s2 = TOps.Mul(xy, TOps.F(-0.211324865405187f));
                z = TOps.Mul(z, TOps.F(0.577350269189626f));
                x = TOps.Add(x, TOps.Sub(s2, z));

                // Not the same expression: the reference writes `x += s2 - z` but `y = y + s2 - z`,
                // which groups as `(y + s2) - z`. An ULP apart, and the compatibility test knows it.
                y = TOps.Sub(TOps.Add(y, s2), z);
                z = TOps.Add(z, TOps.Mul(xy, TOps.F(0.577350269189626f)));
                break;
            }

            case TransformType3D.ImproveXZPlanes:
            {
                TF xz = TOps.Add(x, z);
                TF s2 = TOps.Mul(xz, TOps.F(-0.211324865405187f));
                y = TOps.Mul(y, TOps.F(0.577350269189626f));
                x = TOps.Add(x, TOps.Sub(s2, y));
                z = TOps.Add(z, TOps.Sub(s2, y));
                y = TOps.Add(y, TOps.Mul(xz, TOps.F(0.577350269189626f)));
                break;
            }

            case TransformType3D.DefaultOpenSimplex2:
            {
                TF r = TOps.Mul(TOps.Add(TOps.Add(x, y), z), TOps.F(R3));
                x = TOps.Sub(r, x);
                y = TOps.Sub(r, y);
                z = TOps.Sub(r, z);
                break;
            }

            default:
                break;
        }
    }

    /// <summary>Samples one octave in 2D from already-transformed coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Single2<TOps, TF, TI>(in KernelConfig config, TI seed, TF x, TF y)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => config.NoiseType switch
        {
            NoiseType.OpenSimplex2 => OpenSimplex2Kernel.Sample2<TOps, TF, TI>(seed, x, y),
            NoiseType.Cellular => CellularKernel.Sample2<TOps, TF, TI>(
                seed, x, y, config.CellularDistance, config.CellularReturn, config.CellularJitter),
            NoiseType.Perlin => PerlinKernel.Sample2<TOps, TF, TI>(seed, x, y),
            NoiseType.ValueCubic => ValueCubicKernel.Sample2<TOps, TF, TI>(seed, x, y),
            NoiseType.Value => ValueKernel.Sample2<TOps, TF, TI>(seed, x, y),
            _ => TOps.F(0f),
        };

    /// <summary>Samples one octave in 3D from already-transformed coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Single3<TOps, TF, TI>(in KernelConfig config, TI seed, TF x, TF y, TF z)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => config.NoiseType switch
        {
            NoiseType.OpenSimplex2 => OpenSimplex2Kernel.Sample3<TOps, TF, TI>(seed, x, y, z),
            NoiseType.Cellular => CellularKernel.Sample3<TOps, TF, TI>(
                seed, x, y, z, config.CellularDistance, config.CellularReturn, config.CellularJitter),
            NoiseType.Perlin => PerlinKernel.Sample3<TOps, TF, TI>(seed, x, y, z),
            NoiseType.ValueCubic => ValueCubicKernel.Sample3<TOps, TF, TI>(seed, x, y, z),
            NoiseType.Value => ValueKernel.Sample3<TOps, TF, TI>(seed, x, y, z),
            _ => TOps.F(0f),
        };
}
