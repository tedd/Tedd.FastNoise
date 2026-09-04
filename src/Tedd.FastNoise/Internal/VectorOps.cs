using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Tedd.FastNoise.Internal;

/// <summary>
/// Wide instantiation of <see cref="ISimdOps{TF, TI}"/> over <see cref="Vector{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Vector{T}"/> is deliberately preferred over a fixed <c>Vector256</c>/<c>Vector512</c>
/// path: the JIT selects the widest register set the CPU actually has (AVX-512 where enabled,
/// AVX2, SSE, NEON on ARM) and degrades to a software vector when there is none. One source of
/// truth, no per-architecture branches, and the fallback is automatic.
/// </para>
/// <para>
/// Every operation here is a single instruction on mainstream hardware. The two that are not --
/// <see cref="MulI"/> below SSE4.1 and <see cref="Floor"/> below SSE4.1 -- are emulated by the
/// JIT rather than by us.
/// </para>
/// </remarks>
internal readonly unsafe struct VectorOps : ISimdOps<Vector<float>, Vector<int>>
{
    public static int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Vector<float>.Count;
    }

    public static bool IsAccelerated
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Vector.IsHardwareAccelerated;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> F(float value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> I(int value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Ramp(float start, float step)
        => new Vector<float>(start) + (new Vector<float>(step) * Vector.ConvertToSingle(Vector<int>.Indices));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Add(Vector<float> a, Vector<float> b) => a + b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Sub(Vector<float> a, Vector<float> b) => a - b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Mul(Vector<float> a, Vector<float> b) => a * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Div(Vector<float> a, Vector<float> b) => a / b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Neg(Vector<float> a) => -a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Abs(Vector<float> a) => Vector.Abs(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Min(Vector<float> a, Vector<float> b) => Vector.Min(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Max(Vector<float> a, Vector<float> b) => Vector.Max(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Sqrt(Vector<float> a) => Vector.SquareRoot(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Floor(Vector<float> a) => Vector.Floor(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> ToInt(Vector<float> a) => Vector.ConvertToInt32(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> ToFloat(Vector<int> a) => Vector.ConvertToSingle(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> AddI(Vector<int> a, Vector<int> b) => a + b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> SubI(Vector<int> a, Vector<int> b) => a - b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> MulI(Vector<int> a, Vector<int> b) => a * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> AndI(Vector<int> a, Vector<int> b) => a & b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> OrI(Vector<int> a, Vector<int> b) => a | b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> XorI(Vector<int> a, Vector<int> b) => a ^ b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> NotI(Vector<int> a) => ~a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> ShiftLeft(Vector<int> a, int count) => Vector.ShiftLeft(a, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> ShiftRightArithmetic(Vector<int> a, int count) => Vector.ShiftRightArithmetic(a, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> ShiftRightLogical(Vector<int> a, int count) => Vector.ShiftRightLogical(a, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> LessThanI(Vector<int> a, Vector<int> b) => Vector.LessThan(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> EqualI(Vector<int> a, Vector<int> b) => Vector.Equals(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> LessThanF(Vector<float> a, Vector<float> b) => Vector.AsVectorInt32(Vector.LessThan(a, b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> GreaterThanF(Vector<float> a, Vector<float> b) => Vector.AsVectorInt32(Vector.GreaterThan(a, b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> SelectF(Vector<int> mask, Vector<float> ifTrue, Vector<float> ifFalse)
        => Vector.ConditionalSelect(Vector.AsVectorSingle(mask), ifTrue, ifFalse);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<int> SelectI(Vector<int> mask, Vector<int> ifTrue, Vector<int> ifFalse)
        => Vector.ConditionalSelect(mask, ifTrue, ifFalse);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> FlipSign(Vector<float> value, Vector<int> signBits)
        => Vector.AsVectorSingle(Vector.AsVectorInt32(value) ^ (signBits & new Vector<int>(unchecked((int)0x8000_0000))));

    /// <summary>
    /// Per-lane table read. Uses a hardware gather where the register width and instruction set
    /// line up, and falls back to a spill-and-index loop everywhere else (including ARM).
    /// </summary>
    /// <remarks>
    /// Both branches are decided on constants the JIT folds, so exactly one survives in the
    /// compiled loop. Which one is faster is hardware-dependent and genuinely not obvious --
    /// <c>vgatherdps</c> has high latency but frees the load ports -- so the benchmark project
    /// measures both. See <c>GatherStrategy</c> there.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public static Vector<float> Gather(ReadOnlySpan<float> table, Vector<int> indices)
    {
        ref float origin = ref MemoryMarshal.GetReference(table);

        if (Avx2.IsSupported && Vector<float>.Count == Vector256<float>.Count)
        {
            Vector256<int> idx = Unsafe.As<Vector<int>, Vector256<int>>(ref indices);
            Vector256<float> gathered = Avx2.GatherVector256((float*)Unsafe.AsPointer(ref origin), idx, 4);
            return Unsafe.As<Vector256<float>, Vector<float>>(ref gathered);
        }

        return GatherSoftware(table, indices);
    }

    /// <summary>
    /// Per-lane table read without hardware support: spill the index vector, do independent scalar
    /// loads, rebuild the vector.
    /// </summary>
    /// <remarks>
    /// The universal fallback, and also the thing the hardware path is measured against -- see
    /// <c>GatherStrategy</c> in the benchmark project. Kept separately callable for that reason.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    internal static Vector<float> GatherSoftware(ReadOnlySpan<float> table, Vector<int> indices)
    {
        ref float origin = ref MemoryMarshal.GetReference(table);

        Unsafe.SkipInit(out LaneIndices indexBuffer);
        Unsafe.SkipInit(out LaneValues valueBuffer);

        Span<int> laneIndices = indexBuffer;
        indices.CopyTo(laneIndices);

        Span<float> values = valueBuffer;
        for (int lane = 0; lane < Vector<float>.Count; lane++)
        {
            values[lane] = Unsafe.Add(ref origin, (nint)(uint)laneIndices[lane]);
        }

        return new Vector<float>(values);
    }

    /// <summary>Stack scratch for one vector of indices. Sized for the widest register the runtime supports.</summary>
    [InlineArray(MaxLanes)]
    private struct LaneIndices
    {
        private int _element0;
    }

    /// <summary>Stack scratch for one vector of gathered values.</summary>
    [InlineArray(MaxLanes)]
    private struct LaneValues
    {
        private float _element0;
    }

    /// <summary>Lane count of a 512-bit float vector: the widest <see cref="Vector{T}"/> the runtime will choose.</summary>
    private const int MaxLanes = 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Load(ReadOnlySpan<float> source) => new(source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store(Vector<float> value, Span<float> destination, int index)
        => Unsafe.WriteUnaligned(
            ref Unsafe.As<float, byte>(ref Unsafe.Add(ref MemoryMarshal.GetReference(destination), (nint)(uint)index)),
            value);
}
