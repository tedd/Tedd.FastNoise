// Emits CPU operations and shares exact scalar semantics with compile-time constant folding.
// Saturating casts and explicit precision keep exceptional inputs deterministic across generated backends.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using Tedd.FastNoise.Internal;
using Tedd.FastNoise.Internal.Kernels;

namespace Tedd.FastNoise.Procedural;

internal static class ProceduralRuntime
{
    internal static Type ClrType(ProceduralType type) => type switch
    {
        ProceduralType.Float32 => typeof(float), ProceduralType.Float64 => typeof(double),
        ProceduralType.Int32 => typeof(int), ProceduralType.UInt32 => typeof(uint), ProceduralType.Bool => typeof(bool),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    public static double Read(ReadOnlySpan<double> values, int index) => values[index];
    public static void Write(Span<double> values, int index, double value) => values[index] = value;
    public static int ToInt32(double value) => double.IsNaN(value) ? 0 : value <= int.MinValue ? int.MinValue : value >= int.MaxValue ? int.MaxValue : (int)value;
    public static uint ToUInt32(double value) => double.IsNaN(value) || value <= 0 ? 0 : value >= uint.MaxValue ? uint.MaxValue : (uint)value;
    public static int AbsInt32(int value) => value < 0 ? unchecked(-value) : value;
    public static uint Hash(uint value)
    {
        unchecked
        {
            value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15;
            value *= 0x846ca68b; value ^= value >> 16; return value;
        }
    }
    public static double Lookup(IReadOnlyList<double> values, int index) => values[Math.Clamp(index, 0, values.Count - 1)];
    internal static MethodInfo Method([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type, string name, params Type[] parameters)
        => type.GetMethod(name, parameters) ?? throw new MissingMethodException(type.FullName, name);
    internal static Expression Call([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type, string name, params Expression[] arguments)
    {
        Type[] types = new Type[arguments.Length];
        for (int i = 0; i < types.Length; i++) types[i] = arguments[i].Type;
        return Expression.Call(Method(type, name, types), arguments);
    }
    internal static double ConvertBoundary(double value, ProceduralType type) => type switch
    {
        ProceduralType.Float32 => (float)value, ProceduralType.Float64 => value,
        ProceduralType.Int32 => ToInt32(value), ProceduralType.UInt32 => ToUInt32(value),
        ProceduralType.Bool => value != 0 ? 1d : 0d, _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    internal static Expression ToBoundary(Expression value) => value.Type == typeof(bool)
        ? Expression.Condition(value, Expression.Constant(1d), Expression.Constant(0d))
        : Expression.Convert(value, typeof(double));
    internal static Expression FromBoundary(Expression value, ProceduralType type) => type switch
    {
        ProceduralType.Bool => Expression.NotEqual(value, Expression.Constant(0d)),
        ProceduralType.Int32 => Call(typeof(ProceduralRuntime), nameof(ToInt32), value),
        ProceduralType.UInt32 => Call(typeof(ProceduralRuntime), nameof(ToUInt32), value),
        _ => Expression.Convert(value, ClrType(type)),
    };
    internal static Expression Constant(double value, ProceduralType type) => type switch
    {
        ProceduralType.Float32 => Expression.Constant((float)value), ProceduralType.Float64 => Expression.Constant(value),
        ProceduralType.Int32 => Expression.Constant(ToInt32(value)), ProceduralType.UInt32 => Expression.Constant(ToUInt32(value)),
        ProceduralType.Bool => Expression.Constant(value != 0), _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    internal static double Fold(ProceduralNode node, IReadOnlyList<ProceduralNode> nodes)
    {
        return EvaluateNode(node, nodes[node.A].Constant, node.B < 0 ? 0 : nodes[node.B].Constant,
            node.C < 0 ? 0 : nodes[node.C].Constant, nodes[node.A].Type,
            node.NoiseRequest is { } request ? new ProceduralNoise(request) : null);
    }
    internal static double EvaluateNode(ProceduralNode node, double a, double b, double c, ProceduralType sourceType, ProceduralNoise? noise)
    {
        switch (node.Operation)
        {
            case ProceduralOperation.Constant: return node.Constant;
            case ProceduralOperation.Select: return a != 0 ? b : c;
            case ProceduralOperation.Convert:
                if (sourceType == ProceduralType.Int32 && node.Type == ProceduralType.UInt32) return unchecked((uint)(int)a);
                if (sourceType == ProceduralType.UInt32 && node.Type == ProceduralType.Int32) return unchecked((int)(uint)a);
                return ConvertBoundary(a, node.Type);
            case ProceduralOperation.Hash: return Hash((uint)a);
            case ProceduralOperation.Lookup: return Lookup(node.Table!, (int)a);
            case ProceduralOperation.Noise2D: return noise!.Sample2((float)a, (float)b);
            case ProceduralOperation.Noise3D: return noise!.Sample3((float)a, (float)b, (float)c);
            case ProceduralOperation.And: return a != 0 && b != 0 ? 1 : 0;
            case ProceduralOperation.Or: return a != 0 || b != 0 ? 1 : 0;
            case ProceduralOperation.Not: return a == 0 ? 1 : 0;
            case ProceduralOperation.Equal: return a == b ? 1 : 0;
            case ProceduralOperation.NotEqual: return a != b ? 1 : 0;
            case ProceduralOperation.LessThan: return a < b ? 1 : 0;
            case ProceduralOperation.LessThanOrEqual: return a <= b ? 1 : 0;
            case ProceduralOperation.GreaterThan: return a > b ? 1 : 0;
            case ProceduralOperation.GreaterThanOrEqual: return a >= b ? 1 : 0;
        }
        return sourceType switch
        {
            ProceduralType.Float32 => Floating<float>(node.Operation, (float)a, (float)b),
            ProceduralType.Float64 => Floating<double>(node.Operation, a, b),
            ProceduralType.Int32 => Integer<int>(node.Operation, (int)a, (int)b),
            ProceduralType.UInt32 => Integer<uint>(node.Operation, (uint)a,
                node.Operation is ProceduralOperation.ShiftLeft or ProceduralOperation.ShiftRight ? unchecked((uint)(int)b) : (uint)b),
            _ => throw new NotSupportedException($"Unsupported {sourceType} operation {node.Operation}."),
        };
    }
    private static double Numeric<T>(ProceduralOperation operation, T a, T b) where T : INumber<T>
    {
        T result = unchecked(operation switch
        {
            ProceduralOperation.Add => a + b, ProceduralOperation.Subtract => a - b,
            ProceduralOperation.Multiply => a * b, ProceduralOperation.Divide => a / b,
            ProceduralOperation.Min => T.Min(a, b), ProceduralOperation.Max => T.Max(a, b),
            ProceduralOperation.Negate => -a,
            _ => throw new NotSupportedException($"Unsupported numeric operation {operation}."),
        });
        return double.CreateChecked(result);
    }
    private static double Floating<T>(ProceduralOperation operation, T a, T b) where T : IFloatingPointIeee754<T>
        => operation switch
        {
            ProceduralOperation.Abs => double.CreateChecked(T.Abs(a)), ProceduralOperation.Floor => double.CreateChecked(T.Floor(a)),
            ProceduralOperation.Sqrt => double.CreateChecked(T.Sqrt(a)), ProceduralOperation.Pow => double.CreateChecked(T.Pow(a, b)),
            _ => Numeric(operation, a, b),
        };
    private static double Integer<T>(ProceduralOperation operation, T a, T b) where T : IBinaryInteger<T>
    {
        T result;
        switch (operation)
        {
            case ProceduralOperation.BitAnd: result = a & b; break;
            case ProceduralOperation.BitOr: result = a | b; break;
            case ProceduralOperation.BitXor: result = a ^ b; break;
            case ProceduralOperation.ShiftLeft: result = a << (int.CreateTruncating(b) & 31); break;
            case ProceduralOperation.ShiftRight: result = a >> (int.CreateTruncating(b) & 31); break;
            case ProceduralOperation.Abs: result = T.IsNegative(a) ? unchecked(-a) : a; break;
            default: return Numeric(operation, a, b);
        }
        return double.CreateChecked(result);
    }
    internal static Expression Emit(ProceduralNode node, Func<int, Expression> operand)
    {
        if (node.Operation == ProceduralOperation.Constant) return Constant(node.Constant, node.Type);
        var a = operand(node.A);
        Expression b() => operand(node.B);
        Expression math(string name, params Expression[] arguments) => Call(a.Type == typeof(float) ? typeof(MathF) : typeof(Math), name, arguments);
        return node.Operation switch
        {
            ProceduralOperation.Add => Expression.Add(a, b()),
            ProceduralOperation.Subtract => Expression.Subtract(a, b()),
            ProceduralOperation.Multiply => Expression.Multiply(a, b()),
            ProceduralOperation.Divide => Expression.Divide(a, b()),
            ProceduralOperation.Min => math(nameof(Math.Min), a, b()),
            ProceduralOperation.Max => math(nameof(Math.Max), a, b()),
            ProceduralOperation.Abs => a.Type == typeof(uint) ? a : a.Type == typeof(int)
                ? Call(typeof(ProceduralRuntime), nameof(AbsInt32), a) : math(nameof(Math.Abs), a),
            ProceduralOperation.Floor => math(nameof(Math.Floor), a),
            ProceduralOperation.Sqrt => math(nameof(Math.Sqrt), a),
            ProceduralOperation.Pow => math(nameof(Math.Pow), a, b()),
            ProceduralOperation.Negate => Expression.Negate(a),
            ProceduralOperation.LessThan => Expression.LessThan(a, b()),
            ProceduralOperation.LessThanOrEqual => Expression.LessThanOrEqual(a, b()),
            ProceduralOperation.GreaterThan => Expression.GreaterThan(a, b()),
            ProceduralOperation.GreaterThanOrEqual => Expression.GreaterThanOrEqual(a, b()),
            ProceduralOperation.Equal => Expression.Equal(a, b()),
            ProceduralOperation.NotEqual => Expression.NotEqual(a, b()),
            ProceduralOperation.And or ProceduralOperation.BitAnd => Expression.And(a, b()),
            ProceduralOperation.Or or ProceduralOperation.BitOr => Expression.Or(a, b()),
            ProceduralOperation.Not => Expression.Not(a),
            ProceduralOperation.BitXor => Expression.ExclusiveOr(a, b()),
            ProceduralOperation.ShiftLeft => Expression.LeftShift(a, b()),
            ProceduralOperation.ShiftRight => Expression.RightShift(a, b()),
            ProceduralOperation.Select => Expression.Condition(a, b(), operand(node.C)),
            ProceduralOperation.Convert => Convert(a, node.Type),
            ProceduralOperation.Hash => Call(typeof(ProceduralRuntime), nameof(Hash), a),
            ProceduralOperation.Lookup => FromBoundary(Call(typeof(ProceduralRuntime), nameof(Lookup),
                Expression.Constant(node.Table, typeof(IReadOnlyList<double>)), a), node.Type),
            ProceduralOperation.Noise2D => Expression.Call(Expression.Constant(new ProceduralNoise(node.NoiseRequest!.Value)), Method(typeof(ProceduralNoise), nameof(ProceduralNoise.Sample2), typeof(float), typeof(float)), a, b()),
            ProceduralOperation.Noise3D => Expression.Call(Expression.Constant(new ProceduralNoise(node.NoiseRequest!.Value)), Method(typeof(ProceduralNoise), nameof(ProceduralNoise.Sample3), typeof(float), typeof(float), typeof(float)), a, b(), operand(node.C)),
            _ => throw new NotSupportedException($"Unsupported procedural operation {node.Operation}."),
        };
    }
    private static Expression Convert(Expression value, ProceduralType type)
    {
        if (type == ProceduralType.Bool) return Expression.NotEqual(value, Expression.Default(value.Type));
        if (value.Type == typeof(bool)) return Expression.Condition(value, Constant(1, type), Constant(0, type));
        if (value.Type == typeof(float) || value.Type == typeof(double)) return FromBoundary(ToBoundary(value), type);
        return Expression.Convert(value, ClrType(type));
    }
}

internal sealed class ProceduralNoise
{
    private readonly NoiseFillRequest3D _request;
    private readonly KernelConfig _kernel;
    private readonly FractalConfig _fractal;
    internal ProceduralNoise(NoiseFillRequest3D request)
    {
        _request = request;
        var generator = new NoiseGenerator(request.Seed) { NoiseType = request.NoiseType, RotationType3D = request.RotationType3D,
            CellularDistanceFunction = request.CellularDistance, CellularReturnType = request.CellularReturn, CellularJitter = request.CellularJitter };
        _kernel = generator.BuildKernelConfig();
        _fractal = new() { Type = request.FractalType, Octaves = request.Octaves, Lacunarity = request.Lacunarity,
            Gain = request.Gain, WeightedStrength = request.WeightedStrength, PingPongStrength = request.PingPongStrength, Bounding = request.FractalBounding };
    }
    public float Sample2(float x, float y)
    {
        NoisePipeline.Transform2<ScalarOps, float, int>(_kernel.NoiseType, _request.Frequency, ref x, ref y);
        return FractalKernel.Fractal2<ScalarOps, float, int>(_kernel, _fractal, _request.Seed, x, y, _request.Octaves, _request.LastOctaveFade);
    }
    public float Sample3(float x, float y, float z)
    {
        NoisePipeline.Transform3<ScalarOps, float, int>(_kernel.Transform3D, _request.Frequency, ref x, ref y, ref z);
        return FractalKernel.Fractal3<ScalarOps, float, int>(_kernel, _fractal, _request.Seed, x, y, z, _request.Octaves, _request.LastOctaveFade);
    }
    public Vector<float> Sample2Wide(Vector<float> x, Vector<float> y)
    {
        NoisePipeline.Transform2<VectorOps, Vector<float>, Vector<int>>(_kernel.NoiseType, _request.Frequency, ref x, ref y);
        return FractalKernel.Fractal2<VectorOps, Vector<float>, Vector<int>>(_kernel, _fractal, _request.Seed, x, y, _request.Octaves, _request.LastOctaveFade);
    }
    public Vector<float> Sample3Wide(Vector<float> x, Vector<float> y, Vector<float> z)
    {
        NoisePipeline.Transform3<VectorOps, Vector<float>, Vector<int>>(_kernel.Transform3D, _request.Frequency, ref x, ref y, ref z);
        return FractalKernel.Fractal3<VectorOps, Vector<float>, Vector<int>>(_kernel, _fractal, _request.Seed, x, y, z, _request.Octaves, _request.LastOctaveFade);
    }
}
