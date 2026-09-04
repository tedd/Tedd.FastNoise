using System;

namespace Tedd.FastNoise.Internal;

/// <summary>
/// The lane-width-agnostic operation set the noise kernels are written against.
/// </summary>
/// <typeparam name="TF">Floating-point lane type: <see cref="float"/> for scalar, <c>Vector&lt;float&gt;</c> for SIMD.</typeparam>
/// <typeparam name="TI">Integer lane type: <see cref="int"/> for scalar, <c>Vector&lt;int&gt;</c> for SIMD.</typeparam>
/// <remarks>
/// <para>
/// Every noise algorithm in this library is written exactly once, generically over this interface.
/// The scalar and SIMD backends are two instantiations of the same source, so they cannot drift
/// apart the way hand-duplicated kernels do -- an invariant the test suite asserts directly.
/// </para>
/// <para>
/// Static abstract members on a <c>struct</c> type argument are resolved to direct calls at JIT
/// time and then inlined, so the generic indirection costs nothing at runtime. This is the same
/// shape the BCL uses for <c>TensorPrimitives</c>.
/// </para>
/// <para>
/// Deliberately absent: fused multiply-add. FMA would change results by a fraction of an ULP
/// relative to the scalar path, and this library guarantees bit-identical output across backends
/// so that a client and server running different hardware agree on the terrain.
/// </para>
/// <para>Comparisons return an integer mask: all-ones for true, zero for false, per lane.</para>
/// </remarks>
internal interface ISimdOps<TF, TI>
    where TF : struct
    where TI : struct
{
    /// <summary>Lanes processed per operation. 1 for scalar.</summary>
    static abstract int Count { get; }

    /// <summary>True when this instantiation maps onto hardware vector instructions.</summary>
    static abstract bool IsAccelerated { get; }

    // ---- float construction -------------------------------------------------

    /// <summary>Broadcasts a scalar to every lane.</summary>
    static abstract TF F(float value);

    /// <summary>Broadcasts an integer to every lane.</summary>
    static abstract TI I(int value);

    /// <summary><c>start + step * (0, 1, 2, ... Count-1)</c>. Builds the x coordinates of one SIMD step of a grid row.</summary>
    static abstract TF Ramp(float start, float step);

    // ---- float arithmetic ---------------------------------------------------

    static abstract TF Add(TF a, TF b);
    static abstract TF Sub(TF a, TF b);
    static abstract TF Mul(TF a, TF b);
    static abstract TF Div(TF a, TF b);
    static abstract TF Neg(TF a);
    static abstract TF Abs(TF a);
    static abstract TF Min(TF a, TF b);
    static abstract TF Max(TF a, TF b);
    static abstract TF Sqrt(TF a);
    static abstract TF Floor(TF a);

    // ---- conversion ---------------------------------------------------------

    /// <summary>Truncates toward zero and converts to integer lanes.</summary>
    static abstract TI ToInt(TF a);

    /// <summary>Converts integer lanes to float lanes.</summary>
    static abstract TF ToFloat(TI a);

    // ---- integer arithmetic and bit twiddling -------------------------------

    static abstract TI AddI(TI a, TI b);
    static abstract TI SubI(TI a, TI b);
    static abstract TI MulI(TI a, TI b);
    static abstract TI AndI(TI a, TI b);
    static abstract TI OrI(TI a, TI b);
    static abstract TI XorI(TI a, TI b);
    static abstract TI NotI(TI a);
    static abstract TI ShiftLeft(TI a, int count);
    static abstract TI ShiftRightArithmetic(TI a, int count);
    static abstract TI ShiftRightLogical(TI a, int count);

    // ---- comparison and selection -------------------------------------------

    /// <summary>Per-lane <c>a &lt; b</c> over integers, as a mask.</summary>
    static abstract TI LessThanI(TI a, TI b);

    /// <summary>Per-lane <c>a == b</c> over integers, as a mask.</summary>
    static abstract TI EqualI(TI a, TI b);

    /// <summary>Per-lane <c>a &lt; b</c> over floats, as an integer mask.</summary>
    static abstract TI LessThanF(TF a, TF b);

    /// <summary>Per-lane <c>a &gt; b</c> over floats, as an integer mask.</summary>
    static abstract TI GreaterThanF(TF a, TF b);

    /// <summary>Per-lane <c>mask != 0 ? ifTrue : ifFalse</c> over floats.</summary>
    static abstract TF SelectF(TI mask, TF ifTrue, TF ifFalse);

    /// <summary>Per-lane <c>mask != 0 ? ifTrue : ifFalse</c> over integers.</summary>
    static abstract TI SelectI(TI mask, TI ifTrue, TI ifFalse);

    /// <summary>
    /// Flips the sign of <paramref name="value"/> in lanes where the sign bit of <paramref name="signBits"/> is set.
    /// A single XOR: much cheaper than a compare-and-select.
    /// </summary>
    static abstract TF FlipSign(TF value, TI signBits);

    // ---- table access -------------------------------------------------------

    /// <summary>
    /// Reads <c>table[indices[lane]]</c> for every lane.
    /// </summary>
    /// <remarks>
    /// The one operation with no single-instruction portable form. Callers must have masked
    /// <paramref name="indices"/> into range already -- the implementations do not bounds check,
    /// because this sits in the innermost loop of every gradient evaluation.
    /// </remarks>
    static abstract TF Gather(ReadOnlySpan<float> table, TI indices);

    // ---- memory -------------------------------------------------------------

    /// <summary>Reads <see cref="Count"/> lanes starting at the beginning of <paramref name="source"/>.</summary>
    static abstract TF Load(ReadOnlySpan<float> source);

    /// <summary>Writes <see cref="Count"/> lanes to the beginning of <paramref name="destination"/>.</summary>
    static abstract void Store(TF value, Span<float> destination);
}
