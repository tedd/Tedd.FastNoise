using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal.Kernels;

/// <summary>
/// OpenSimplex2: simplex-lattice gradient noise. The 2D case is ordinary simplex noise; the 3D
/// case is built from two offset rotated cube grids.
/// </summary>
/// <remarks>
/// <para>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md.
/// </para>
/// <para>
/// The reference walks a nested if/else to decide which simplex the sample landed in, and breaks
/// out of a two-pass loop early. Neither survives vectorisation, so both are rewritten here as
/// mask arithmetic: every candidate is computed and the wrong ones are selected away. That costs a
/// few extra multiplies per lane and buys the whole kernel a wide path. The gradient lookups --
/// the expensive part -- are not duplicated: the *inputs* are selected, then one gradient is
/// evaluated, so the operation count against the reference is unchanged.
/// </para>
/// <para>
/// The caller is expected to have applied the skew (2D) or rotation (3D) already; see
/// <c>CoordinateTransform</c>.
/// </para>
/// </remarks>
internal static class OpenSimplex2Kernel
{
    private const float Sqrt3 = 1.7320508075688772935274463415059f;

    /// <summary>Unskew factor for the 2D triangular lattice.</summary>
    private const float G2 = (3 - Sqrt3) / 6;

    /// <summary>Precomputed coefficient of the third corner falloff, folded out of the reference expression.</summary>
    private const float C1 = 2 * (1 - 2 * G2) * (1 / G2 - 2);

    /// <summary>Precomputed constant term of the third corner falloff.</summary>
    private const float C2 = -2 * (1 - 2 * G2) * (1 - 2 * G2);

    /// <summary>Normalisation constant carried over from the reference implementation.</summary>
    private const float Scale2D = 99.83685446303647f;

    /// <summary>Normalisation constant carried over from the reference implementation.</summary>
    private const float Scale3D = 32.69428253173828125f;

    /// <summary>Samples 2D OpenSimplex2 noise on pre-skewed coordinates. Output is approximately [-1, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Sample2<TOps, TF, TI>(TI seed, TF x, TF y)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI i = NoiseMath.FastFloor<TOps, TF, TI>(x);
        TI j = NoiseMath.FastFloor<TOps, TF, TI>(y);

        TF xi = TOps.Sub(x, TOps.ToFloat(i));
        TF yi = TOps.Sub(y, TOps.ToFloat(j));

        TF t = TOps.Mul(TOps.Add(xi, yi), TOps.F(G2));
        TF x0 = TOps.Sub(xi, t);
        TF y0 = TOps.Sub(yi, t);

        i = Hashing.Prime<TOps, TF, TI>(i, Hashing.PrimeX);
        j = Hashing.Prime<TOps, TF, TI>(j, Hashing.PrimeY);
        TI px = TOps.I(Hashing.PrimeX);
        TI py = TOps.I(Hashing.PrimeY);

        // Corner 0: the cell origin.
        TF a = TOps.Sub(TOps.Sub(TOps.F(0.5f), TOps.Mul(x0, x0)), TOps.Mul(y0, y0));
        TF n0 = Falloff2<TOps, TF, TI>(a, Hashing.GradCoord2<TOps, TF, TI>(seed, i, j, x0, y0));

        // Corner 2: the opposite cell corner. Its falloff is derived from `a` rather than recomputed.
        TF c = TOps.Add(TOps.Mul(TOps.F(C1), t), TOps.Add(TOps.F(C2), a));
        TF x2 = TOps.Add(x0, TOps.F(2 * G2 - 1));
        TF y2 = TOps.Add(y0, TOps.F(2 * G2 - 1));
        TF n2 = Falloff2<TOps, TF, TI>(
            c,
            Hashing.GradCoord2<TOps, TF, TI>(seed, TOps.AddI(i, px), TOps.AddI(j, py), x2, y2));

        // Corner 1: whichever of the two triangles in this cell the point fell into.
        TI upper = TOps.GreaterThanF(y0, x0);
        TF x1 = TOps.Add(x0, TOps.SelectF(upper, TOps.F(G2), TOps.F(G2 - 1)));
        TF y1 = TOps.Add(y0, TOps.SelectF(upper, TOps.F(G2 - 1), TOps.F(G2)));
        TI i1 = TOps.AddI(i, TOps.SelectI(upper, TOps.I(0), px));
        TI j1 = TOps.AddI(j, TOps.SelectI(upper, py, TOps.I(0)));

        TF b = TOps.Sub(TOps.Sub(TOps.F(0.5f), TOps.Mul(x1, x1)), TOps.Mul(y1, y1));
        TF n1 = Falloff2<TOps, TF, TI>(b, Hashing.GradCoord2<TOps, TF, TI>(seed, i1, j1, x1, y1));

        return TOps.Mul(TOps.Add(TOps.Add(n0, n1), n2), TOps.F(Scale2D));
    }

    /// <summary>Applies the quartic radial falloff, or zero where the corner is out of range.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TF Falloff2<TOps, TF, TI>(TF t, TF gradient)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI inside = TOps.GreaterThanF(t, TOps.F(0f));
        TF t2 = TOps.Mul(t, t);
        return TOps.SelectF(inside, TOps.Mul(TOps.Mul(t2, t2), gradient), TOps.F(0f));
    }

    /// <summary>Samples 3D OpenSimplex2 noise on pre-rotated coordinates. Output is approximately [-1, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Sample3<TOps, TF, TI>(TI seed, TF x, TF y, TF z)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI i = NoiseMath.FastRound<TOps, TF, TI>(x);
        TI j = NoiseMath.FastRound<TOps, TF, TI>(y);
        TI k = NoiseMath.FastRound<TOps, TF, TI>(z);

        TF x0 = TOps.Sub(x, TOps.ToFloat(i));
        TF y0 = TOps.Sub(y, TOps.ToFloat(j));
        TF z0 = TOps.Sub(z, TOps.ToFloat(k));

        // -1 when the offset is positive, +1 when negative: which neighbouring lattice point is closer.
        TI one = TOps.I(1);
        TI xNSign = TOps.OrI(TOps.ToInt(TOps.Sub(TOps.F(-1f), x0)), one);
        TI yNSign = TOps.OrI(TOps.ToInt(TOps.Sub(TOps.F(-1f), y0)), one);
        TI zNSign = TOps.OrI(TOps.ToInt(TOps.Sub(TOps.F(-1f), z0)), one);

        TF ax0 = TOps.Mul(TOps.ToFloat(xNSign), TOps.Neg(x0));
        TF ay0 = TOps.Mul(TOps.ToFloat(yNSign), TOps.Neg(y0));
        TF az0 = TOps.Mul(TOps.ToFloat(zNSign), TOps.Neg(z0));

        i = Hashing.Prime<TOps, TF, TI>(i, Hashing.PrimeX);
        j = Hashing.Prime<TOps, TF, TI>(j, Hashing.PrimeY);
        k = Hashing.Prime<TOps, TF, TI>(k, Hashing.PrimeZ);

        TI px = TOps.I(Hashing.PrimeX);
        TI py = TOps.I(Hashing.PrimeY);
        TI pz = TOps.I(Hashing.PrimeZ);
        TF zero = TOps.F(0f);

        TF value = zero;
        TF a = TOps.Sub(TOps.Sub(TOps.F(0.6f), TOps.Mul(x0, x0)), TOps.Add(TOps.Mul(y0, y0), TOps.Mul(z0, z0)));

        // Two passes over the two offset lattices. The reference loops with an early break; unrolled
        // here so the second pass can specialise away the work that only feeds a third iteration.
        for (int pass = 0; ; pass++)
        {
            // The corner the sample sits in.
            {
                TI inside = TOps.GreaterThanF(a, zero);
                TF a2 = TOps.Mul(a, a);
                TF contribution = TOps.Mul(TOps.Mul(a2, a2), Hashing.GradCoord3<TOps, TF, TI>(seed, i, j, k, x0, y0, z0));
                value = TOps.Add(value, TOps.SelectF(inside, contribution, zero));
            }

            // The nearest face neighbour, along whichever axis the sample leans toward.
            {
                TI leanX = TOps.AndI(TOps.NotI(TOps.LessThanF(ax0, ay0)), TOps.NotI(TOps.LessThanF(ax0, az0)));
                TI leanY = TOps.AndI(
                    TOps.NotI(leanX),
                    TOps.AndI(TOps.GreaterThanF(ay0, ax0), TOps.NotI(TOps.LessThanF(ay0, az0))));
                TI leanZ = TOps.NotI(TOps.OrI(leanX, leanY));

                TF axis = TOps.SelectF(leanX, ax0, TOps.SelectF(leanY, ay0, az0));
                TF b = TOps.Sub(TOps.Add(TOps.Add(a, axis), axis), TOps.F(1f));

                TI ni = TOps.SubI(i, TOps.SelectI(leanX, TOps.MulI(xNSign, px), TOps.I(0)));
                TI nj = TOps.SubI(j, TOps.SelectI(leanY, TOps.MulI(yNSign, py), TOps.I(0)));
                TI nk = TOps.SubI(k, TOps.SelectI(leanZ, TOps.MulI(zNSign, pz), TOps.I(0)));

                TF nx = TOps.Add(x0, TOps.SelectF(leanX, TOps.ToFloat(xNSign), zero));
                TF ny = TOps.Add(y0, TOps.SelectF(leanY, TOps.ToFloat(yNSign), zero));
                TF nz = TOps.Add(z0, TOps.SelectF(leanZ, TOps.ToFloat(zNSign), zero));

                TI inside = TOps.GreaterThanF(b, zero);
                TF b2 = TOps.Mul(b, b);
                TF contribution = TOps.Mul(TOps.Mul(b2, b2), Hashing.GradCoord3<TOps, TF, TI>(seed, ni, nj, nk, nx, ny, nz));
                value = TOps.Add(value, TOps.SelectF(inside, contribution, zero));
            }

            if (pass == 1)
            {
                break;
            }

            // Step onto the second, half-offset lattice.
            ax0 = TOps.Sub(TOps.F(0.5f), ax0);
            ay0 = TOps.Sub(TOps.F(0.5f), ay0);
            az0 = TOps.Sub(TOps.F(0.5f), az0);

            x0 = TOps.Mul(TOps.ToFloat(xNSign), ax0);
            y0 = TOps.Mul(TOps.ToFloat(yNSign), ay0);
            z0 = TOps.Mul(TOps.ToFloat(zNSign), az0);

            a = TOps.Add(a, TOps.Sub(TOps.Sub(TOps.F(0.75f), ax0), TOps.Add(ay0, az0)));

            i = TOps.AddI(i, TOps.AndI(TOps.ShiftRightArithmetic(xNSign, 1), px));
            j = TOps.AddI(j, TOps.AndI(TOps.ShiftRightArithmetic(yNSign, 1), py));
            k = TOps.AddI(k, TOps.AndI(TOps.ShiftRightArithmetic(zNSign, 1), pz));

            xNSign = TOps.SubI(TOps.I(0), xNSign);
            yNSign = TOps.SubI(TOps.I(0), yNSign);
            zNSign = TOps.SubI(TOps.I(0), zNSign);

            seed = TOps.NotI(seed);
        }

        return TOps.Mul(value, TOps.F(Scale3D));
    }
}
