using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal;

/// <summary>
/// The lattice hash, the value lookup and the gradient dot products every kernel is built from.
/// </summary>
/// <remarks>
/// <para>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md. The constants,
/// the shift widths and the operation order are reproduced exactly, because "produces the same
/// values as FastNoiseLite" is a feature -- existing worlds keep generating the same terrain -- and
/// the test suite asserts it bit for bit against a vendored copy of the original.
/// </para>
/// <para>
/// What is not reproduced is the shape: everything here is generic over <see cref="ISimdOps{TF, TI}"/>,
/// so one body serves the scalar and the wide paths.
/// </para>
/// </remarks>
internal static class Hashing
{
    /// <summary>Odd multiplier decorrelating the X lattice axis.</summary>
    public const int PrimeX = 501125321;

    /// <summary>Odd multiplier decorrelating the Y lattice axis.</summary>
    public const int PrimeY = 1136930381;

    /// <summary>Odd multiplier decorrelating the Z lattice axis.</summary>
    public const int PrimeZ = 1720413743;

    /// <summary>Avalanche multiplier applied once the axes are combined.</summary>
    private const int Mixer = 0x27D4EB2D;

    /// <summary>Scales a full-range <see cref="int"/> to [-1, 1).</summary>
    private const float IntToUnit = 1f / 2147483648f;

    /// <summary>Multiplies an integer lattice coordinate by its axis prime.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TI Prime<TOps, TF, TI>(TI coordinate, int prime)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => TOps.MulI(coordinate, TOps.I(prime));

    /// <summary>Hashes a 2D primed lattice coordinate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TI Hash2<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => TOps.MulI(TOps.XorI(TOps.XorI(seed, xPrimed), yPrimed), TOps.I(Mixer));

    /// <summary>Hashes a 3D primed lattice coordinate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TI Hash3<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed, TI zPrimed)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => TOps.MulI(TOps.XorI(TOps.XorI(TOps.XorI(seed, xPrimed), yPrimed), zPrimed), TOps.I(Mixer));

    /// <summary>A pseudo-random value in [-1, 1) for a 2D lattice point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF ValCoord2<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI hash = Hash2<TOps, TF, TI>(seed, xPrimed, yPrimed);
        hash = TOps.MulI(hash, hash);
        hash = TOps.XorI(hash, TOps.ShiftLeft(hash, 19));
        return TOps.Mul(TOps.ToFloat(hash), TOps.F(IntToUnit));
    }

    /// <summary>A pseudo-random value in [-1, 1) for a 3D lattice point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF ValCoord3<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed, TI zPrimed)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI hash = Hash3<TOps, TF, TI>(seed, xPrimed, yPrimed, zPrimed);
        hash = TOps.MulI(hash, hash);
        hash = TOps.XorI(hash, TOps.ShiftLeft(hash, 19));
        return TOps.Mul(TOps.ToFloat(hash), TOps.F(IntToUnit));
    }

    /// <summary>Dot product of the distance vector with the 2D gradient chosen by the lattice hash.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF GradCoord2<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed, TF xd, TF yd)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI hash = Hash2<TOps, TF, TI>(seed, xPrimed, yPrimed);
        hash = TOps.XorI(hash, TOps.ShiftRightArithmetic(hash, 15));
        hash = TOps.AndI(hash, TOps.I(127 << 1));

        TF xg = TOps.Gather(Tables.Gradients2D, hash);
        TF yg = TOps.Gather(Tables.Gradients2D, TOps.OrI(hash, TOps.I(1)));

        return TOps.Add(TOps.Mul(xd, xg), TOps.Mul(yd, yg));
    }

    /// <summary>Dot product of the distance vector with the 3D gradient chosen by the lattice hash.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF GradCoord3<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed, TI zPrimed, TF xd, TF yd, TF zd)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI hash = Hash3<TOps, TF, TI>(seed, xPrimed, yPrimed, zPrimed);
        hash = TOps.XorI(hash, TOps.ShiftRightArithmetic(hash, 15));
        hash = TOps.AndI(hash, TOps.I(63 << 2));

        TF xg = TOps.Gather(Tables.Gradients3D, hash);
        TF yg = TOps.Gather(Tables.Gradients3D, TOps.OrI(hash, TOps.I(1)));
        TF zg = TOps.Gather(Tables.Gradients3D, TOps.OrI(hash, TOps.I(2)));

        return TOps.Add(TOps.Add(TOps.Mul(xd, xg), TOps.Mul(yd, yg)), TOps.Mul(zd, zg));
    }

    /// <summary>The 2D feature-point offset for a cellular lattice cell.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GradCoordOut2<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed, out TF xo, out TF yo)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI hash = TOps.AndI(Hash2<TOps, TF, TI>(seed, xPrimed, yPrimed), TOps.I(255 << 1));
        xo = TOps.Gather(Tables.RandVecs2D, hash);
        yo = TOps.Gather(Tables.RandVecs2D, TOps.OrI(hash, TOps.I(1)));
    }

    /// <summary>The 3D feature-point offset for a cellular lattice cell.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GradCoordOut3<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed, TI zPrimed, out TF xo, out TF yo, out TF zo)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI hash = TOps.AndI(Hash3<TOps, TF, TI>(seed, xPrimed, yPrimed, zPrimed), TOps.I(255 << 2));
        xo = TOps.Gather(Tables.RandVecs3D, hash);
        yo = TOps.Gather(Tables.RandVecs3D, TOps.OrI(hash, TOps.I(1)));
        zo = TOps.Gather(Tables.RandVecs3D, TOps.OrI(hash, TOps.I(2)));
    }

    /// <summary>
    /// The 2D gradient dot product and a feature-point direction from a single hash, for domain warping.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GradCoordDual2<TOps, TF, TI>(TI seed, TI xPrimed, TI yPrimed, TF xd, TF yd, out TF xo, out TF yo)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI hash = Hash2<TOps, TF, TI>(seed, xPrimed, yPrimed);
        TI index1 = TOps.AndI(hash, TOps.I(127 << 1));
        TI index2 = TOps.AndI(TOps.ShiftRightArithmetic(hash, 7), TOps.I(255 << 1));

        TF xg = TOps.Gather(Tables.Gradients2D, index1);
        TF yg = TOps.Gather(Tables.Gradients2D, TOps.OrI(index1, TOps.I(1)));
        TF value = TOps.Add(TOps.Mul(xd, xg), TOps.Mul(yd, yg));

        TF xgo = TOps.Gather(Tables.RandVecs2D, index2);
        TF ygo = TOps.Gather(Tables.RandVecs2D, TOps.OrI(index2, TOps.I(1)));

        xo = TOps.Mul(value, xgo);
        yo = TOps.Mul(value, ygo);
    }

    /// <summary>
    /// The 3D gradient dot product and a feature-point direction from a single hash, for domain warping.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GradCoordDual3<TOps, TF, TI>(
        TI seed, TI xPrimed, TI yPrimed, TI zPrimed, TF xd, TF yd, TF zd, out TF xo, out TF yo, out TF zo)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI hash = Hash3<TOps, TF, TI>(seed, xPrimed, yPrimed, zPrimed);
        TI index1 = TOps.AndI(hash, TOps.I(63 << 2));
        TI index2 = TOps.AndI(TOps.ShiftRightArithmetic(hash, 6), TOps.I(255 << 2));

        TF xg = TOps.Gather(Tables.Gradients3D, index1);
        TF yg = TOps.Gather(Tables.Gradients3D, TOps.OrI(index1, TOps.I(1)));
        TF zg = TOps.Gather(Tables.Gradients3D, TOps.OrI(index1, TOps.I(2)));
        TF value = TOps.Add(TOps.Add(TOps.Mul(xd, xg), TOps.Mul(yd, yg)), TOps.Mul(zd, zg));

        TF xgo = TOps.Gather(Tables.RandVecs3D, index2);
        TF ygo = TOps.Gather(Tables.RandVecs3D, TOps.OrI(index2, TOps.I(1)));
        TF zgo = TOps.Gather(Tables.RandVecs3D, TOps.OrI(index2, TOps.I(2)));

        xo = TOps.Mul(value, xgo);
        yo = TOps.Mul(value, ygo);
        zo = TOps.Mul(value, zgo);
    }
}
