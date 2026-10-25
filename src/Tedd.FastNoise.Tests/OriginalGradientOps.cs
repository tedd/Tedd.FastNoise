// Test-only operation set: preserve the released gather lookup while sharing unchanged kernel arithmetic.
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Tedd.FastNoise.Internal;

namespace Tedd.FastNoise.Tests;

internal readonly struct OriginalGradientOps : ISimdOps<Vector<float>, Vector<int>>
{
    public static int Count => VectorOps.Count;
    public static bool IsAccelerated => VectorOps.IsAccelerated;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> F(float value) => VectorOps.F(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> I(int value) => VectorOps.I(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Ramp(float start, float step) => VectorOps.Ramp(start, step);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Add(Vector<float> a, Vector<float> b) => VectorOps.Add(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Sub(Vector<float> a, Vector<float> b) => VectorOps.Sub(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Mul(Vector<float> a, Vector<float> b) => VectorOps.Mul(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Div(Vector<float> a, Vector<float> b) => VectorOps.Div(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Neg(Vector<float> a) => VectorOps.Neg(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Abs(Vector<float> a) => VectorOps.Abs(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Min(Vector<float> a, Vector<float> b) => VectorOps.Min(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Max(Vector<float> a, Vector<float> b) => VectorOps.Max(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Sqrt(Vector<float> a) => VectorOps.Sqrt(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Floor(Vector<float> a) => VectorOps.Floor(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> ToInt(Vector<float> a) => VectorOps.ToInt(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> ToFloat(Vector<int> a) => VectorOps.ToFloat(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> AddI(Vector<int> a, Vector<int> b) => VectorOps.AddI(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> SubI(Vector<int> a, Vector<int> b) => VectorOps.SubI(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> MulI(Vector<int> a, Vector<int> b) => VectorOps.MulI(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> AndI(Vector<int> a, Vector<int> b) => VectorOps.AndI(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> OrI(Vector<int> a, Vector<int> b) => VectorOps.OrI(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> XorI(Vector<int> a, Vector<int> b) => VectorOps.XorI(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> NotI(Vector<int> a) => VectorOps.NotI(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> ShiftLeft(Vector<int> a, int count) => VectorOps.ShiftLeft(a, count);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> ShiftRightArithmetic(Vector<int> a, int count) => VectorOps.ShiftRightArithmetic(a, count);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> ShiftRightLogical(Vector<int> a, int count) => VectorOps.ShiftRightLogical(a, count);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> LessThanI(Vector<int> a, Vector<int> b) => VectorOps.LessThanI(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> EqualI(Vector<int> a, Vector<int> b) => VectorOps.EqualI(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> LessThanF(Vector<float> a, Vector<float> b) => VectorOps.LessThanF(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> GreaterThanF(Vector<float> a, Vector<float> b) => VectorOps.GreaterThanF(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> SelectF(Vector<int> mask, Vector<float> yes, Vector<float> no) => VectorOps.SelectF(mask, yes, no);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> SelectI(Vector<int> mask, Vector<int> yes, Vector<int> no) => VectorOps.SelectI(mask, yes, no);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> FlipSign(Vector<float> value, Vector<int> signBits) => VectorOps.FlipSign(value, signBits);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Gather(ReadOnlySpan<float> table, Vector<int> indices) => VectorOps.Gather(table, indices);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<int> GatherI(ReadOnlySpan<int> table, Vector<int> indices) => VectorOps.GatherI(table, indices);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector<float> Load(ReadOnlySpan<float> source) => VectorOps.Load(source);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Store(Vector<float> value, Span<float> destination, int index) => VectorOps.Store(value, destination, index);
}
