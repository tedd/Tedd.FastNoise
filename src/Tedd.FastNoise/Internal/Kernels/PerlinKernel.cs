using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal.Kernels;

/// <summary>
/// Gradient noise on a cubic lattice: the classic Perlin construction.
/// </summary>
/// <remarks>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md.
/// Cheap and well understood, at the cost of mild axis alignment in the output -- large smooth
/// features tend to line up with the lattice. For heightmaps that is usually invisible; for 3D
/// density fields prefer <see cref="OpenSimplex2Kernel"/>.
/// </remarks>
internal static class PerlinKernel
{
    /// <summary>Normalisation constant carried over from the reference implementation.</summary>
    private const float Scale2D = 1.4247691104677813f;

    /// <summary>Normalisation constant carried over from the reference implementation.</summary>
    private const float Scale3D = 0.964921414852142333984375f;

    /// <summary>Samples 2D Perlin noise. Output is approximately [-1, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Sample2<TOps, TF, TI>(TI seed, TF x, TF y)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI x0 = NoiseMath.FastFloor<TOps, TF, TI>(x);
        TI y0 = NoiseMath.FastFloor<TOps, TF, TI>(y);

        TF xd0 = TOps.Sub(x, TOps.ToFloat(x0));
        TF yd0 = TOps.Sub(y, TOps.ToFloat(y0));
        TF xd1 = TOps.Sub(xd0, TOps.F(1f));
        TF yd1 = TOps.Sub(yd0, TOps.F(1f));

        TF xs = NoiseMath.InterpQuintic<TOps, TF, TI>(xd0);
        TF ys = NoiseMath.InterpQuintic<TOps, TF, TI>(yd0);

        x0 = Hashing.Prime<TOps, TF, TI>(x0, Hashing.PrimeX);
        y0 = Hashing.Prime<TOps, TF, TI>(y0, Hashing.PrimeY);
        TI x1 = TOps.AddI(x0, TOps.I(Hashing.PrimeX));
        TI y1 = TOps.AddI(y0, TOps.I(Hashing.PrimeY));

        TF xf0 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.GradCoord2<TOps, TF, TI>(seed, x0, y0, xd0, yd0),
            Hashing.GradCoord2<TOps, TF, TI>(seed, x1, y0, xd1, yd0), xs);
        TF xf1 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.GradCoord2<TOps, TF, TI>(seed, x0, y1, xd0, yd1),
            Hashing.GradCoord2<TOps, TF, TI>(seed, x1, y1, xd1, yd1), xs);

        return TOps.Mul(NoiseMath.Lerp<TOps, TF, TI>(xf0, xf1, ys), TOps.F(Scale2D));
    }

    /// <summary>Samples 3D Perlin noise. Output is approximately [-1, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Sample3<TOps, TF, TI>(TI seed, TF x, TF y, TF z)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI x0 = NoiseMath.FastFloor<TOps, TF, TI>(x);
        TI y0 = NoiseMath.FastFloor<TOps, TF, TI>(y);
        TI z0 = NoiseMath.FastFloor<TOps, TF, TI>(z);

        TF xd0 = TOps.Sub(x, TOps.ToFloat(x0));
        TF yd0 = TOps.Sub(y, TOps.ToFloat(y0));
        TF zd0 = TOps.Sub(z, TOps.ToFloat(z0));
        TF xd1 = TOps.Sub(xd0, TOps.F(1f));
        TF yd1 = TOps.Sub(yd0, TOps.F(1f));
        TF zd1 = TOps.Sub(zd0, TOps.F(1f));

        TF xs = NoiseMath.InterpQuintic<TOps, TF, TI>(xd0);
        TF ys = NoiseMath.InterpQuintic<TOps, TF, TI>(yd0);
        TF zs = NoiseMath.InterpQuintic<TOps, TF, TI>(zd0);

        x0 = Hashing.Prime<TOps, TF, TI>(x0, Hashing.PrimeX);
        y0 = Hashing.Prime<TOps, TF, TI>(y0, Hashing.PrimeY);
        z0 = Hashing.Prime<TOps, TF, TI>(z0, Hashing.PrimeZ);
        TI x1 = TOps.AddI(x0, TOps.I(Hashing.PrimeX));
        TI y1 = TOps.AddI(y0, TOps.I(Hashing.PrimeY));
        TI z1 = TOps.AddI(z0, TOps.I(Hashing.PrimeZ));

        TF xf00 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.GradCoord3<TOps, TF, TI>(seed, x0, y0, z0, xd0, yd0, zd0),
            Hashing.GradCoord3<TOps, TF, TI>(seed, x1, y0, z0, xd1, yd0, zd0), xs);
        TF xf10 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.GradCoord3<TOps, TF, TI>(seed, x0, y1, z0, xd0, yd1, zd0),
            Hashing.GradCoord3<TOps, TF, TI>(seed, x1, y1, z0, xd1, yd1, zd0), xs);
        TF xf01 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.GradCoord3<TOps, TF, TI>(seed, x0, y0, z1, xd0, yd0, zd1),
            Hashing.GradCoord3<TOps, TF, TI>(seed, x1, y0, z1, xd1, yd0, zd1), xs);
        TF xf11 = NoiseMath.Lerp<TOps, TF, TI>(
            Hashing.GradCoord3<TOps, TF, TI>(seed, x0, y1, z1, xd0, yd1, zd1),
            Hashing.GradCoord3<TOps, TF, TI>(seed, x1, y1, z1, xd1, yd1, zd1), xs);

        TF yf0 = NoiseMath.Lerp<TOps, TF, TI>(xf00, xf10, ys);
        TF yf1 = NoiseMath.Lerp<TOps, TF, TI>(xf01, xf11, ys);

        return TOps.Mul(NoiseMath.Lerp<TOps, TF, TI>(yf0, yf1, zs), TOps.F(Scale3D));
    }
}
