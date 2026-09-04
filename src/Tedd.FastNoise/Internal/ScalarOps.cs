using System;
using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal;

/// <summary>
/// One-lane instantiation of <see cref="ISimdOps{TF, TI}"/>: plain <see cref="float"/> and <see cref="int"/> maths.
/// </summary>
/// <remarks>
/// This is the fallback for hardware without vector support, the implementation behind every
/// single-point sampling API, and the correctness oracle the SIMD path is tested against.
/// </remarks>
internal readonly struct ScalarOps : ISimdOps<float, int>
{
    public static int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1;
    }

    public static bool IsAccelerated
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float F(float value) => value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int I(int value) => value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Ramp(float start, float step) => start;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Add(float a, float b) => a + b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sub(float a, float b) => a - b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Mul(float a, float b) => a * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Div(float a, float b) => a / b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Neg(float a) => -a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Abs(float a) => MathF.Abs(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min(float a, float b) => MathF.Min(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max(float a, float b) => MathF.Max(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sqrt(float a) => MathF.Sqrt(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Floor(float a) => MathF.Floor(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt(float a) => (int)a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToFloat(int a) => a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AddI(int a, int b) => a + b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SubI(int a, int b) => a - b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MulI(int a, int b) => a * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AndI(int a, int b) => a & b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrI(int a, int b) => a | b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int XorI(int a, int b) => a ^ b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int NotI(int a) => ~a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ShiftLeft(int a, int count) => a << count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ShiftRightArithmetic(int a, int count) => a >> count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ShiftRightLogical(int a, int count) => (int)((uint)a >> count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LessThanI(int a, int b) => a < b ? -1 : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EqualI(int a, int b) => a == b ? -1 : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LessThanF(float a, float b) => a < b ? -1 : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GreaterThanF(float a, float b) => a > b ? -1 : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SelectF(int mask, float ifTrue, float ifFalse) => mask != 0 ? ifTrue : ifFalse;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SelectI(int mask, int ifTrue, int ifFalse) => mask != 0 ? ifTrue : ifFalse;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FlipSign(float value, int signBits)
        => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) ^ (signBits & unchecked((int)0x8000_0000)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Gather(ReadOnlySpan<float> table, int indices)
        => System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(table), (nint)(uint)indices);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Load(ReadOnlySpan<float> source) => source[0];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store(float value, Span<float> destination) => destination[0] = value;
}
