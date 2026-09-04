using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal.Kernels;

/// <summary>
/// Value noise with cubic rather than linear interpolation over a 4x4 (or 4x4x4) neighbourhood.
/// </summary>
/// <remarks>
/// <para>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md.
/// </para>
/// <para>
/// Smoother than plain value noise because the cubic passes through the lattice values with
/// continuous slope, which suppresses the boxy look. It is also the most expensive kernel here:
/// 3D reads 64 lattice points per sample against Perlin's 8. Budget accordingly -- this is a
/// "one layer, low frequency" tool, not something to run at eight octaves per voxel.
/// </para>
/// </remarks>
internal static class ValueCubicKernel
{
    /// <summary>Cubic interpolation overshoots [-1, 1]; the reference divides it back by 1.5 per axis.</summary>
    private const float Scale2D = 1f / (1.5f * 1.5f);

    /// <summary>Cubic interpolation overshoots [-1, 1]; the reference divides it back by 1.5 per axis.</summary>
    private const float Scale3D = 1f / (1.5f * 1.5f * 1.5f);

    /// <summary>Samples 2D cubic value noise. Output is approximately [-1, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Sample2<TOps, TF, TI>(TI seed, TF x, TF y)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI x1 = NoiseMath.FastFloor<TOps, TF, TI>(x);
        TI y1 = NoiseMath.FastFloor<TOps, TF, TI>(y);

        TF xs = TOps.Sub(x, TOps.ToFloat(x1));
        TF ys = TOps.Sub(y, TOps.ToFloat(y1));

        x1 = Hashing.Prime<TOps, TF, TI>(x1, Hashing.PrimeX);
        y1 = Hashing.Prime<TOps, TF, TI>(y1, Hashing.PrimeY);

        TI px = TOps.I(Hashing.PrimeX);
        TI py = TOps.I(Hashing.PrimeY);
        TI px2 = TOps.I(unchecked(Hashing.PrimeX * 2));
        TI py2 = TOps.I(unchecked(Hashing.PrimeY * 2));

        TI x0 = TOps.SubI(x1, px);
        TI y0 = TOps.SubI(y1, py);
        TI x2 = TOps.AddI(x1, px);
        TI y2 = TOps.AddI(y1, py);
        TI x3 = TOps.AddI(x1, px2);
        TI y3 = TOps.AddI(y1, py2);

        TF result = NoiseMath.CubicLerp<TOps, TF, TI>(
            Row2<TOps, TF, TI>(seed, x0, x1, x2, x3, y0, xs),
            Row2<TOps, TF, TI>(seed, x0, x1, x2, x3, y1, xs),
            Row2<TOps, TF, TI>(seed, x0, x1, x2, x3, y2, xs),
            Row2<TOps, TF, TI>(seed, x0, x1, x2, x3, y3, xs),
            ys);

        return TOps.Mul(result, TOps.F(Scale2D));
    }

    /// <summary>Cubic blend of the four lattice values along x at one y row.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TF Row2<TOps, TF, TI>(TI seed, TI x0, TI x1, TI x2, TI x3, TI yn, TF xs)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => NoiseMath.CubicLerp<TOps, TF, TI>(
            Hashing.ValCoord2<TOps, TF, TI>(seed, x0, yn),
            Hashing.ValCoord2<TOps, TF, TI>(seed, x1, yn),
            Hashing.ValCoord2<TOps, TF, TI>(seed, x2, yn),
            Hashing.ValCoord2<TOps, TF, TI>(seed, x3, yn),
            xs);

    /// <summary>Samples 3D cubic value noise. Output is approximately [-1, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Sample3<TOps, TF, TI>(TI seed, TF x, TF y, TF z)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI x1 = NoiseMath.FastFloor<TOps, TF, TI>(x);
        TI y1 = NoiseMath.FastFloor<TOps, TF, TI>(y);
        TI z1 = NoiseMath.FastFloor<TOps, TF, TI>(z);

        TF xs = TOps.Sub(x, TOps.ToFloat(x1));
        TF ys = TOps.Sub(y, TOps.ToFloat(y1));
        TF zs = TOps.Sub(z, TOps.ToFloat(z1));

        x1 = Hashing.Prime<TOps, TF, TI>(x1, Hashing.PrimeX);
        y1 = Hashing.Prime<TOps, TF, TI>(y1, Hashing.PrimeY);
        z1 = Hashing.Prime<TOps, TF, TI>(z1, Hashing.PrimeZ);

        TI px = TOps.I(Hashing.PrimeX);
        TI py = TOps.I(Hashing.PrimeY);
        TI pz = TOps.I(Hashing.PrimeZ);

        TI x0 = TOps.SubI(x1, px);
        TI y0 = TOps.SubI(y1, py);
        TI z0 = TOps.SubI(z1, pz);
        TI x2 = TOps.AddI(x1, px);
        TI y2 = TOps.AddI(y1, py);
        TI z2 = TOps.AddI(z1, pz);
        TI x3 = TOps.AddI(x1, TOps.I(unchecked(Hashing.PrimeX * 2)));
        TI y3 = TOps.AddI(y1, TOps.I(unchecked(Hashing.PrimeY * 2)));
        TI z3 = TOps.AddI(z1, TOps.I(unchecked(Hashing.PrimeZ * 2)));

        TF result = NoiseMath.CubicLerp<TOps, TF, TI>(
            Plane3<TOps, TF, TI>(seed, x0, x1, x2, x3, y0, y1, y2, y3, z0, xs, ys),
            Plane3<TOps, TF, TI>(seed, x0, x1, x2, x3, y0, y1, y2, y3, z1, xs, ys),
            Plane3<TOps, TF, TI>(seed, x0, x1, x2, x3, y0, y1, y2, y3, z2, xs, ys),
            Plane3<TOps, TF, TI>(seed, x0, x1, x2, x3, y0, y1, y2, y3, z3, xs, ys),
            zs);

        return TOps.Mul(result, TOps.F(Scale3D));
    }

    /// <summary>Cubic blend of one 4x4 xy plane at a fixed z.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TF Plane3<TOps, TF, TI>(
        TI seed, TI x0, TI x1, TI x2, TI x3, TI y0, TI y1, TI y2, TI y3, TI zn, TF xs, TF ys)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => NoiseMath.CubicLerp<TOps, TF, TI>(
            Row3<TOps, TF, TI>(seed, x0, x1, x2, x3, y0, zn, xs),
            Row3<TOps, TF, TI>(seed, x0, x1, x2, x3, y1, zn, xs),
            Row3<TOps, TF, TI>(seed, x0, x1, x2, x3, y2, zn, xs),
            Row3<TOps, TF, TI>(seed, x0, x1, x2, x3, y3, zn, xs),
            ys);

    /// <summary>Cubic blend of the four lattice values along x at one (y, z).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TF Row3<TOps, TF, TI>(TI seed, TI x0, TI x1, TI x2, TI x3, TI yn, TI zn, TF xs)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => NoiseMath.CubicLerp<TOps, TF, TI>(
            Hashing.ValCoord3<TOps, TF, TI>(seed, x0, yn, zn),
            Hashing.ValCoord3<TOps, TF, TI>(seed, x1, yn, zn),
            Hashing.ValCoord3<TOps, TF, TI>(seed, x2, yn, zn),
            Hashing.ValCoord3<TOps, TF, TI>(seed, x3, yn, zn),
            xs);
}
