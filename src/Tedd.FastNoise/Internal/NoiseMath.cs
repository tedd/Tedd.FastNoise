using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal;

/// <summary>
/// Rounding and interpolation helpers, written once over <see cref="ISimdOps{TF, TI}"/>.
/// </summary>
/// <remarks>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md. The exact
/// forms matter: several of these differ from the obvious implementation in ways that change
/// results, and matching them is what keeps this library bit-compatible.
/// </remarks>
internal static class NoiseMath
{
    /// <summary>
    /// Largest integer not greater than <paramref name="a"/>.
    /// </summary>
    /// <remarks>
    /// Truncate, then step down for negatives. Note this returns <c>-2</c> for exactly <c>-1.0</c>,
    /// where <c>MathF.Floor</c> returns <c>-1</c>. That is not a defect: at an exact lattice
    /// boundary the point is assigned to the cell below with a fractional offset of 1.0, which
    /// interpolates to the identical value. Reproduced as-is for compatibility.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TI FastFloor<TOps, TF, TI>(TF a)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        // Comparison masks are all-ones (-1) when true, so adding the mask is the conditional decrement.
        TI truncated = TOps.ToInt(a);
        return TOps.AddI(truncated, TOps.LessThanF(a, TOps.F(0f)));
    }

    /// <summary>Nearest integer, halves away from zero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TI FastRound<TOps, TF, TI>(TF a)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI negative = TOps.LessThanF(a, TOps.F(0f));
        TF nudged = TOps.Add(a, TOps.SelectF(negative, TOps.F(-0.5f), TOps.F(0.5f)));
        return TOps.ToInt(nudged);
    }

    /// <summary><c>a + t * (b - a)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF Lerp<TOps, TF, TI>(TF a, TF b, TF t)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => TOps.Add(a, TOps.Mul(t, TOps.Sub(b, a)));

    /// <summary>Cubic ease curve, <c>3t^2 - 2t^3</c>. Used by value noise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF InterpHermite<TOps, TF, TI>(TF t)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
        => TOps.Mul(TOps.Mul(t, t), TOps.Sub(TOps.F(3f), TOps.Mul(TOps.F(2f), t)));

    /// <summary>
    /// Quintic ease curve, <c>6t^5 - 15t^4 + 10t^3</c>. Used by Perlin.
    /// </summary>
    /// <remarks>
    /// Its second derivative vanishes at the lattice points, where the cubic curve's does not.
    /// The difference is invisible in a heightmap image and very visible once you light the mesh
    /// the heightmap becomes: cubic interpolation creases along the lattice.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF InterpQuintic<TOps, TF, TI>(TF t)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        // t * t * t * (t * (t * 6 - 15) + 10)
        TF inner = TOps.Add(TOps.Mul(t, TOps.Sub(TOps.Mul(t, TOps.F(6f)), TOps.F(15f))), TOps.F(10f));
        return TOps.Mul(TOps.Mul(TOps.Mul(t, t), t), inner);
    }

    /// <summary>Catmull-Rom style cubic through four lattice values. Used by cubic value noise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF CubicLerp<TOps, TF, TI>(TF a, TF b, TF c, TF d, TF t)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TF p = TOps.Sub(TOps.Sub(d, c), TOps.Sub(a, b));
        TF t2 = TOps.Mul(t, t);
        TF t3 = TOps.Mul(t2, t);

        // Summed strictly left to right. Float addition is not associative, so regrouping these
        // four terms shifts the result by an ULP and breaks bit-compatibility with the reference.
        TF sum = TOps.Mul(t3, p);
        sum = TOps.Add(sum, TOps.Mul(t2, TOps.Sub(TOps.Sub(a, b), p)));
        sum = TOps.Add(sum, TOps.Mul(t, TOps.Sub(c, a)));
        return TOps.Add(sum, b);
    }

    /// <summary>Folds <paramref name="t"/> back and forth through [0, 1] with period 2.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TF PingPong<TOps, TF, TI>(TF t)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        // Truncation, not floor: matches the reference for the non-negative inputs this sees.
        TF wrapped = TOps.Sub(t, TOps.Mul(TOps.ToFloat(TOps.ToInt(TOps.Mul(t, TOps.F(0.5f)))), TOps.F(2f)));
        TI lower = TOps.LessThanF(wrapped, TOps.F(1f));
        return TOps.SelectF(lower, wrapped, TOps.Sub(TOps.F(2f), wrapped));
    }
}
