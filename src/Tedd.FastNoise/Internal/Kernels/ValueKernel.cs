using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal.Kernels;

/// <summary>
/// Value noise: interpolate a pseudo-random value stored at each lattice point.
/// </summary>
/// <remarks>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md.
/// The cheapest option here -- no gradient dot product and no table gather at all, so it is the
/// only kernel that vectorises with zero memory traffic. The price is visible lattice structure:
/// extrema land on integer coordinates instead of between them. Good for anything that gets
/// thresholded or quantised afterwards (ore scatter, per-block variation, cave masks), poor as a
/// visible height field on its own.
/// </remarks>
internal static class ValueKernel
{
    /// <summary>Samples 2D value noise. Output is [-1, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Sample2<TOps, TF, TI>(TI seed, TF x, TF y)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI x0 = NoiseMath.FastFloor<TOps, TF, TI>(x);
        TI y0 = NoiseMath.FastFloor<TOps, TF, TI>(y);

        TF xs = NoiseMath.InterpHermite<TOps, TF, TI>(TOps.Sub(x, TOps.ToFloat(x0)));
        TF ys = NoiseMath.InterpHermite<TOps, TF, TI>(TOps.Sub(y, TOps.ToFloat(y0)));

        x0 = Hashing.Prime<TOps, TF, TI>(x0, Hashing.PrimeX);
        y0 = Hashing.Prime<TOps, TF, TI>(y0, Hashing.PrimeY);
        TI x1 = TOps.AddI(x0, TOps.I(Hashing.PrimeX));
        TI y1 = TOps.AddI(y0, TOps.I(Hashing.PrimeY));

        TF xf0 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.ValCoord2<TOps, TF, TI>(seed, x0, y0),
            Hashing.ValCoord2<TOps, TF, TI>(seed, x1, y0), xs);
        TF xf1 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.ValCoord2<TOps, TF, TI>(seed, x0, y1),
            Hashing.ValCoord2<TOps, TF, TI>(seed, x1, y1), xs);

        return NoiseMath.Lerp<TOps, TF, TI>(xf0, xf1, ys);
    }

    /// <summary>Samples 3D value noise. Output is [-1, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Sample3<TOps, TF, TI>(TI seed, TF x, TF y, TF z)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI x0 = NoiseMath.FastFloor<TOps, TF, TI>(x);
        TI y0 = NoiseMath.FastFloor<TOps, TF, TI>(y);
        TI z0 = NoiseMath.FastFloor<TOps, TF, TI>(z);

        TF xs = NoiseMath.InterpHermite<TOps, TF, TI>(TOps.Sub(x, TOps.ToFloat(x0)));
        TF ys = NoiseMath.InterpHermite<TOps, TF, TI>(TOps.Sub(y, TOps.ToFloat(y0)));
        TF zs = NoiseMath.InterpHermite<TOps, TF, TI>(TOps.Sub(z, TOps.ToFloat(z0)));

        x0 = Hashing.Prime<TOps, TF, TI>(x0, Hashing.PrimeX);
        y0 = Hashing.Prime<TOps, TF, TI>(y0, Hashing.PrimeY);
        z0 = Hashing.Prime<TOps, TF, TI>(z0, Hashing.PrimeZ);
        TI x1 = TOps.AddI(x0, TOps.I(Hashing.PrimeX));
        TI y1 = TOps.AddI(y0, TOps.I(Hashing.PrimeY));
        TI z1 = TOps.AddI(z0, TOps.I(Hashing.PrimeZ));

        TF xf00 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.ValCoord3<TOps, TF, TI>(seed, x0, y0, z0),
            Hashing.ValCoord3<TOps, TF, TI>(seed, x1, y0, z0), xs);
        TF xf10 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.ValCoord3<TOps, TF, TI>(seed, x0, y1, z0),
            Hashing.ValCoord3<TOps, TF, TI>(seed, x1, y1, z0), xs);
        TF xf01 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.ValCoord3<TOps, TF, TI>(seed, x0, y0, z1),
            Hashing.ValCoord3<TOps, TF, TI>(seed, x1, y0, z1), xs);
        TF xf11 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.ValCoord3<TOps, TF, TI>(seed, x0, y1, z1),
            Hashing.ValCoord3<TOps, TF, TI>(seed, x1, y1, z1), xs);

        TF yf0 = NoiseMath.Lerp<TOps, TF, TI>(xf00, xf10, ys);
        TF yf1 = NoiseMath.Lerp<TOps, TF, TI>(xf01, xf11, ys);

        return NoiseMath.Lerp<TOps, TF, TI>(yf0, yf1, zs);
    }
}
