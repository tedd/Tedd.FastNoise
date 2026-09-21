// Compiles the procedural DAG into full-width SIMD expressions with two double vectors per float vector.
// Explicit Float32 nodes stay in float registers; binary64 nodes preserve precision without reducing noise lane count.
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Numerics;

namespace Tedd.FastNoise.Procedural;

internal readonly record struct DoublePair(Vector<double> Low, Vector<double> High)
{
    public static DoublePair operator +(DoublePair a, DoublePair b) => new(a.Low + b.Low, a.High + b.High);
    public static DoublePair operator -(DoublePair a, DoublePair b) => new(a.Low - b.Low, a.High - b.High);
    public static DoublePair operator *(DoublePair a, DoublePair b) => new(a.Low * b.Low, a.High * b.High);
    public static DoublePair operator /(DoublePair a, DoublePair b) => new(a.Low / b.Low, a.High / b.High);
    public static DoublePair operator -(DoublePair a) => new(-a.Low, -a.High);
}

internal static class ProceduralWideRuntime
{
    internal static Type ClrType(ProceduralType type) => type switch
    {
        ProceduralType.Float32 => typeof(Vector<float>), ProceduralType.Float64 => typeof(DoublePair),
        ProceduralType.UInt32 => typeof(Vector<uint>), _ => typeof(Vector<int>),
    };
    private static Expression Call(string name, params Expression[] args) => ProceduralRuntime.Call(typeof(ProceduralWideRuntime), name, args);
    internal static Expression Constant(double value, ProceduralType type) => type switch
    {
        ProceduralType.Float32 => Expression.Constant(new Vector<float>((float)value)),
        ProceduralType.Float64 => Expression.Constant(new DoublePair(new(value), new(value))),
        ProceduralType.Int32 => Expression.Constant(new Vector<int>(ProceduralRuntime.ToInt32(value))),
        ProceduralType.UInt32 => Expression.Constant(new Vector<uint>(ProceduralRuntime.ToUInt32(value))),
        ProceduralType.Bool => Expression.Constant(new Vector<int>(value != 0 ? -1 : 0)),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    internal static Expression Read(ParameterExpression inputs, int index, int stride, ProceduralType type)
        => Call("Read" + type, inputs, Expression.Constant(index), Expression.Constant(stride));
    internal static Expression ToOutput(Expression value, ProceduralType type)
        => type == ProceduralType.Bool ? Call(nameof(BoolToDouble), value)
            : type == ProceduralType.Float64 ? value : Call(nameof(ToDouble), value);
    internal static Expression Emit(ProceduralNode node, Func<int, Expression> operand, Func<int, ProceduralType> operandType)
    {
        if (node.Operation == ProceduralOperation.Constant) return Constant(node.Constant, node.Type);
        var a = operand(node.A);
        Expression b() => operand(node.B);
        switch (node.Operation)
        {
            case ProceduralOperation.Add: return Expression.Add(a, b());
            case ProceduralOperation.Subtract: return Expression.Subtract(a, b());
            case ProceduralOperation.Multiply: return Expression.Multiply(a, b());
            case ProceduralOperation.Divide: return Expression.Divide(a, b());
            case ProceduralOperation.Negate: return Expression.Negate(a);
            case ProceduralOperation.Min: return Call(nameof(Min), a, b());
            case ProceduralOperation.Max: return Call(nameof(Max), a, b());
            case ProceduralOperation.Abs: return Call(nameof(Abs), a);
            case ProceduralOperation.Sqrt: return Call(nameof(Sqrt), a);
            case ProceduralOperation.Floor: return Call(nameof(Floor), a);
            case ProceduralOperation.Pow: return Call(nameof(Pow), a, b());
            case ProceduralOperation.LessThan: return Call(nameof(LessThan), a, b());
            case ProceduralOperation.LessThanOrEqual: return Call(nameof(LessThanOrEqual), a, b());
            case ProceduralOperation.GreaterThan: return Call(nameof(LessThan), b(), a);
            case ProceduralOperation.GreaterThanOrEqual: return Call(nameof(LessThanOrEqual), b(), a);
            case ProceduralOperation.Equal: return Call(nameof(Equal), a, b());
            case ProceduralOperation.NotEqual: return Expression.Not(Call(nameof(Equal), a, b()));
            case ProceduralOperation.And:
            case ProceduralOperation.BitAnd: return Expression.And(a, b());
            case ProceduralOperation.Or:
            case ProceduralOperation.BitOr: return Expression.Or(a, b());
            case ProceduralOperation.BitXor: return Expression.ExclusiveOr(a, b());
            case ProceduralOperation.Not: return Expression.Not(a);
            case ProceduralOperation.Select: return Call(nameof(Select), a, b(), operand(node.C));
            case ProceduralOperation.Convert:
                var sourceType = operandType(node.A);
                if (node.Type == ProceduralType.Bool) return Expression.Not(Call(nameof(Equal), a, Constant(0, sourceType)));
                if (sourceType == ProceduralType.Bool) return Call(nameof(Select), a, Constant(1, node.Type), Constant(0, node.Type));
                return Call("To" + node.Type, a);
            case ProceduralOperation.Hash: return Call(nameof(Hash), a);
            case ProceduralOperation.ShiftLeft: return Call(nameof(ShiftLeft), a, b());
            case ProceduralOperation.ShiftRight: return Call(nameof(ShiftRight), a, b());
            case ProceduralOperation.Lookup:
                var lookup = Call(nameof(Lookup), Expression.Constant(node.Table, typeof(IReadOnlyList<double>)), a);
                return node.Type == ProceduralType.Float64 ? lookup
                    : node.Type == ProceduralType.Bool ? Expression.Not(Call(nameof(Equal), lookup, Constant(0, ProceduralType.Float64)))
                    : Call("To" + node.Type, lookup);
            case ProceduralOperation.Noise2D:
                return Expression.Call(Expression.Constant(new ProceduralNoise(node.NoiseRequest!.Value)),
                    ProceduralRuntime.Method(typeof(ProceduralNoise), nameof(ProceduralNoise.Sample2Wide), typeof(Vector<float>), typeof(Vector<float>)), a, b());
            case ProceduralOperation.Noise3D:
                return Expression.Call(Expression.Constant(new ProceduralNoise(node.NoiseRequest!.Value)),
                    ProceduralRuntime.Method(typeof(ProceduralNoise), nameof(ProceduralNoise.Sample3Wide), typeof(Vector<float>), typeof(Vector<float>), typeof(Vector<float>)), a, b(), operand(node.C));
            default: throw new NotSupportedException($"Unsupported SIMD operation {node.Operation}.");
        }
    }

    public static DoublePair ReadFloat64(ReadOnlySpan<double> inputs, int index, int stride)
    {
        Span<double> lanes = stackalloc double[Vector<float>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = inputs[index + i * stride];
        return new(new(lanes), new(lanes[Vector<double>.Count..]));
    }
    public static void Write(Span<double> outputs, int index, int stride, DoublePair value)
    {
        for (int i = 0; i < Vector<double>.Count; i++)
        {
            outputs[index + i * stride] = value.Low[i];
            outputs[index + (i + Vector<double>.Count) * stride] = value.High[i];
        }
    }
    public static Vector<float> ToFloat32(DoublePair value) => Vector.Narrow(value.Low, value.High);
    public static Vector<float> ToFloat32(Vector<int> value) => Vector.ConvertToSingle(value);
    public static Vector<float> ToFloat32(Vector<uint> value) => Vector.ConvertToSingle(value);
    public static DoublePair ToFloat64(Vector<float> value) => ToDouble(value);
    public static DoublePair ToFloat64(Vector<int> value) => ToDouble(value);
    public static DoublePair ToFloat64(Vector<uint> value) => ToDouble(value);
    public static DoublePair ToDouble(Vector<float> value) { Vector.Widen(value, out var low, out var high); return new(low, high); }
    public static DoublePair ToDouble(Vector<int> value) { Vector.Widen(value, out var low, out var high); return new(Vector.ConvertToDouble(low), Vector.ConvertToDouble(high)); }
    public static DoublePair ToDouble(Vector<uint> value) { Vector.Widen(value, out var low, out var high); return new(Vector.ConvertToDouble(low), Vector.ConvertToDouble(high)); }
    public static DoublePair BoolToDouble(Vector<int> value) => ToDouble(value & Vector<int>.One);
    public static Vector<int> ToInt32(Vector<uint> value) => Vector.AsVectorInt32(value);
    public static Vector<uint> ToUInt32(Vector<int> value) => Vector.AsVectorUInt32(value);
    public static Vector<int> ToInt32(Vector<float> value) => ToInt32(ToDouble(value));
    public static Vector<uint> ToUInt32(Vector<float> value) => ToUInt32(ToDouble(value));
    public static Vector<int> ToInt32(DoublePair value)
    {
        Span<int> lanes = stackalloc int[Vector<int>.Count];
        for (int i = 0; i < Vector<double>.Count; i++)
        { lanes[i] = ProceduralRuntime.ToInt32(value.Low[i]); lanes[i + Vector<double>.Count] = ProceduralRuntime.ToInt32(value.High[i]); }
        return new(lanes);
    }
    public static Vector<uint> ToUInt32(DoublePair value)
    {
        Span<uint> lanes = stackalloc uint[Vector<uint>.Count];
        for (int i = 0; i < Vector<double>.Count; i++)
        { lanes[i] = ProceduralRuntime.ToUInt32(value.Low[i]); lanes[i + Vector<double>.Count] = ProceduralRuntime.ToUInt32(value.High[i]); }
        return new(lanes);
    }
    public static DoublePair Select(Vector<int> mask, DoublePair yes, DoublePair no)
    {
        Vector.Widen(mask, out var low, out var high);
        return new(Vector.ConditionalSelect(low, yes.Low, no.Low), Vector.ConditionalSelect(high, yes.High, no.High));
    }
    public static Vector<float> Select(Vector<int> mask, Vector<float> yes, Vector<float> no) => Vector.ConditionalSelect(mask, yes, no);
    public static Vector<int> Select(Vector<int> mask, Vector<int> yes, Vector<int> no) => Vector.ConditionalSelect(mask, yes, no);
    public static Vector<uint> Select(Vector<int> mask, Vector<uint> yes, Vector<uint> no) => Vector.ConditionalSelect(Vector.AsVectorUInt32(mask), yes, no);

    // Hardware min/max do not agree uniformly on NaN and signed zero. Explicit selections retain Math/MathF semantics.
    public static Vector<float> Min(Vector<float> a, Vector<float> b)
    {
        var equal = Vector.Equals(a, b);
        var result = Vector.ConditionalSelect(Vector.LessThan(a, b), a, b);
        result = Vector.ConditionalSelect(equal, Vector.BitwiseOr(a, b), result);
        return Vector.ConditionalSelect(Vector.Equals(a, a), result, a);
    }
    public static Vector<float> Max(Vector<float> a, Vector<float> b)
    {
        var equal = Vector.Equals(a, b);
        var result = Vector.ConditionalSelect(Vector.GreaterThan(a, b), a, b);
        result = Vector.ConditionalSelect(equal, Vector.BitwiseAnd(a, b), result);
        return Vector.ConditionalSelect(Vector.Equals(a, a), result, a);
    }
    private static Vector<double> MinDouble(Vector<double> a, Vector<double> b)
    {
        var result = Vector.ConditionalSelect(Vector.LessThan(a, b), a, b);
        result = Vector.ConditionalSelect(Vector.Equals(a, b), Vector.BitwiseOr(a, b), result);
        return Vector.ConditionalSelect(Vector.Equals(a, a), result, a);
    }
    private static Vector<double> MaxDouble(Vector<double> a, Vector<double> b)
    {
        var result = Vector.ConditionalSelect(Vector.GreaterThan(a, b), a, b);
        result = Vector.ConditionalSelect(Vector.Equals(a, b), Vector.BitwiseAnd(a, b), result);
        return Vector.ConditionalSelect(Vector.Equals(a, a), result, a);
    }
    public static DoublePair Min(DoublePair a, DoublePair b) => new(MinDouble(a.Low, b.Low), MinDouble(a.High, b.High));
    public static DoublePair Max(DoublePair a, DoublePair b) => new(MaxDouble(a.Low, b.Low), MaxDouble(a.High, b.High));
    public static Vector<float> Abs(Vector<float> value) => Vector.Abs(value);
    public static DoublePair Abs(DoublePair value) => new(Vector.Abs(value.Low), Vector.Abs(value.High));
    public static Vector<int> Abs(Vector<int> value) => Vector.Abs(value);
    public static Vector<uint> Abs(Vector<uint> value) => value;
    public static Vector<float> Sqrt(Vector<float> value) => Vector.SquareRoot(value);
    public static DoublePair Sqrt(DoublePair value) => new(Vector.SquareRoot(value.Low), Vector.SquareRoot(value.High));
    public static Vector<float> Floor(Vector<float> value) => Vector.Floor(value);
    public static DoublePair Floor(DoublePair value) => new(Vector.Floor(value.Low), Vector.Floor(value.High));
    public static Vector<uint> Hash(Vector<uint> value)
    {
        value ^= value >> 16; value *= new Vector<uint>(0x7feb352d);
        value ^= value >> 15; value *= new Vector<uint>(0x846ca68b); return value ^ (value >> 16);
    }
    public static DoublePair Lookup(IReadOnlyList<double> table, Vector<int> index)
    {
        Span<double> lanes = stackalloc double[Vector<int>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = table[Math.Clamp(index[i], 0, table.Count - 1)];
        return new(new(lanes), new(lanes[Vector<double>.Count..]));
    }
    public static Vector<float> ReadFloat32(ReadOnlySpan<double> inputs, int index, int stride)
    {
        Span<float> lanes = stackalloc float[Vector<float>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = (float)inputs[index + i * stride];
        return new(lanes);
    }
    public static Vector<int> ReadInt32(ReadOnlySpan<double> inputs, int index, int stride)
    {
        Span<int> lanes = stackalloc int[Vector<int>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = ProceduralRuntime.ToInt32(inputs[index + i * stride]);
        return new(lanes);
    }
    public static Vector<uint> ReadUInt32(ReadOnlySpan<double> inputs, int index, int stride)
    {
        Span<uint> lanes = stackalloc uint[Vector<uint>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = ProceduralRuntime.ToUInt32(inputs[index + i * stride]);
        return new(lanes);
    }
    public static Vector<int> ReadBool(ReadOnlySpan<double> inputs, int index, int stride)
    {
        Span<int> lanes = stackalloc int[Vector<int>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = (inputs[index + i * stride] != 0 ? -1 : 0);
        return new(lanes);
    }
    public static Vector<int> LessThan(Vector<float> a, Vector<float> b) => Vector.LessThan(a, b);
    public static Vector<int> LessThan(Vector<int> a, Vector<int> b) => Vector.LessThan(a, b);
    public static Vector<int> LessThan(Vector<uint> a, Vector<uint> b) => Vector.AsVectorInt32(Vector.LessThan(a, b));
    public static Vector<int> LessThan(DoublePair a, DoublePair b) => Vector.Narrow(Vector.LessThan(a.Low, b.Low), Vector.LessThan(a.High, b.High));
    public static Vector<int> LessThanOrEqual(Vector<float> a, Vector<float> b) => Vector.LessThanOrEqual(a, b);
    public static Vector<int> LessThanOrEqual(Vector<int> a, Vector<int> b) => Vector.LessThanOrEqual(a, b);
    public static Vector<int> LessThanOrEqual(Vector<uint> a, Vector<uint> b) => Vector.AsVectorInt32(Vector.LessThanOrEqual(a, b));
    public static Vector<int> LessThanOrEqual(DoublePair a, DoublePair b) => Vector.Narrow(Vector.LessThanOrEqual(a.Low, b.Low), Vector.LessThanOrEqual(a.High, b.High));
    public static Vector<int> Equal(Vector<float> a, Vector<float> b) => Vector.Equals(a, b);
    public static Vector<int> Equal(Vector<int> a, Vector<int> b) => Vector.Equals(a, b);
    public static Vector<int> Equal(Vector<uint> a, Vector<uint> b) => Vector.AsVectorInt32(Vector.Equals(a, b));
    public static Vector<int> Equal(DoublePair a, DoublePair b) => Vector.Narrow(Vector.Equals(a.Low, b.Low), Vector.Equals(a.High, b.High));
    public static Vector<int> Min(Vector<int> a, Vector<int> b) => Vector.Min(a, b);
    public static Vector<uint> Min(Vector<uint> a, Vector<uint> b) => Vector.Min(a, b);
    public static Vector<int> Max(Vector<int> a, Vector<int> b) => Vector.Max(a, b);
    public static Vector<uint> Max(Vector<uint> a, Vector<uint> b) => Vector.Max(a, b);
    public static Vector<int> ShiftLeft(Vector<int> value, Vector<int> count)
    {
        Span<int> lanes = stackalloc int[Vector<int>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = value[i] << (count[i] & 31);
        return new(lanes);
    }
    public static Vector<uint> ShiftLeft(Vector<uint> value, Vector<int> count)
    {
        Span<uint> lanes = stackalloc uint[Vector<uint>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = value[i] << (count[i] & 31);
        return new(lanes);
    }
    public static Vector<int> ShiftRight(Vector<int> value, Vector<int> count)
    {
        Span<int> lanes = stackalloc int[Vector<int>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = value[i] >> (count[i] & 31);
        return new(lanes);
    }
    public static Vector<uint> ShiftRight(Vector<uint> value, Vector<int> count)
    {
        Span<uint> lanes = stackalloc uint[Vector<uint>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = value[i] >> (count[i] & 31);
        return new(lanes);
    }
    public static Vector<float> Pow(Vector<float> value, Vector<float> exponent)
    {
        Span<float> lanes = stackalloc float[Vector<float>.Count];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = MathF.Pow(value[i], exponent[i]);
        return new(lanes);
    }
    public static DoublePair Pow(DoublePair value, DoublePair exponent)
    {
        Span<double> lanes = stackalloc double[Vector<float>.Count];
        for (int i = 0; i < Vector<double>.Count; i++)
        { lanes[i] = Math.Pow(value.Low[i], exponent.Low[i]); lanes[i + Vector<double>.Count] = Math.Pow(value.High[i], exponent.High[i]); }
        return new(new(lanes), new(lanes[Vector<double>.Count..]));
    }
}
