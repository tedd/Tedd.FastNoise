using System;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using Tedd.FastNoise.Internal;

namespace Tedd.FastNoise.Benchmark.Benchmarks;

internal static class OpenSimplex3MechanismData
{
    public const int Vectors = 100_000;

    public static Vector<float>[] Floats(int seed, float minimum = -0.5f, float range = 1f)
    {
        Random random = new(seed);
        Vector<float>[] result = new Vector<float>[Vectors];
        float[] lanes = new float[Vector<float>.Count];
        for (int index = 0; index < result.Length; index++)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                lanes[lane] = minimum + (random.NextSingle() * range);
            }

            result[index] = new Vector<float>(lanes);
        }

        return result;
    }

    public static Vector<int>[] Signs(int seed)
    {
        Random random = new(seed);
        Vector<int>[] result = new Vector<int>[Vectors];
        int[] lanes = new int[Vector<int>.Count];
        for (int index = 0; index < result.Length; index++)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                lanes[lane] = random.Next(2) == 0 ? -1 : 1;
            }

            result[index] = new Vector<int>(lanes);
        }

        return result;
    }

    public static Vector<int>[] Masks(int seed)
    {
        Random random = new(seed);
        Vector<int>[] result = new Vector<int>[Vectors];
        int[] lanes = new int[Vector<int>.Count];
        for (int index = 0; index < result.Length; index++)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                lanes[lane] = random.Next(2) == 0 ? -1 : 0;
            }

            result[index] = new Vector<int>(lanes);
        }

        return result;
    }
}

/// <summary>H-009: replace sign conversion and multiplication with absolute value.</summary>
[NoiseBenchmark]
public class OpenSimplex3InitialMagnitude
{
    private Vector<float>[] _x = [];
    private Vector<float>[] _y = [];
    private Vector<float>[] _z = [];

    [GlobalSetup]
    public void Setup()
    {
        _x = OpenSimplex3MechanismData.Floats(9001);
        _y = OpenSimplex3MechanismData.Floats(9002);
        _z = OpenSimplex3MechanismData.Floats(9003);
    }

    [Benchmark(Baseline = true, Description = "Convert signs and multiply")]
    public float ConvertAndMultiply()
    {
        Vector<float> total = Vector<float>.Zero;
        Vector<float> minusOne = new(-1f);
        for (int index = 0; index < _x.Length; index++)
        {
            Vector<int> sx = Vector.ConvertToInt32(minusOne - _x[index]) | Vector<int>.One;
            Vector<int> sy = Vector.ConvertToInt32(minusOne - _y[index]) | Vector<int>.One;
            Vector<int> sz = Vector.ConvertToInt32(minusOne - _z[index]) | Vector<int>.One;
            total += (Vector.ConvertToSingle(sx) * -_x[index])
                + (Vector.ConvertToSingle(sy) * -_y[index])
                + (Vector.ConvertToSingle(sz) * -_z[index]);
        }

        return Vector.Sum(total);
    }

    [Benchmark(Description = "Vector.Abs")]
    public float AbsoluteValue()
    {
        Vector<float> total = Vector<float>.Zero;
        for (int index = 0; index < _x.Length; index++)
        {
            total += Vector.Abs(_x[index]) + Vector.Abs(_y[index]) + Vector.Abs(_z[index]);
        }

        return Vector.Sum(total);
    }
}

/// <summary>H-010: reconstruct signed offsets with XOR instead of integer conversion and multiply.</summary>
[NoiseBenchmark]
public class OpenSimplex3SignedOffset
{
    private Vector<float>[] _magnitude = [];
    private Vector<int>[] _sign = [];

    [GlobalSetup]
    public void Setup()
    {
        _magnitude = OpenSimplex3MechanismData.Floats(9010, 0f, 0.5f);
        _sign = OpenSimplex3MechanismData.Signs(9011);
    }

    [Benchmark(Baseline = true, Description = "Convert sign and multiply")]
    public float ConvertAndMultiply()
    {
        Vector<float> total = Vector<float>.Zero;
        for (int index = 0; index < _magnitude.Length; index++)
        {
            total += Vector.ConvertToSingle(_sign[index]) * _magnitude[index];
        }

        return Vector.Sum(total);
    }

    [Benchmark(Description = "Flip sign bit")]
    public float FlipSignBit()
    {
        Vector<float> total = Vector<float>.Zero;
        for (int index = 0; index < _magnitude.Length; index++)
        {
            total += VectorOps.FlipSign(_magnitude[index], _sign[index]);
        }

        return Vector.Sum(total);
    }
}

/// <summary>H-011: construct selected ±1 offsets with sign-bit XOR instead of integer conversion.</summary>
[NoiseBenchmark]
public class OpenSimplex3SignedUnit
{
    private Vector<int>[] _sign = [];
    private Vector<int>[] _mask = [];

    [GlobalSetup]
    public void Setup()
    {
        _sign = OpenSimplex3MechanismData.Signs(9020);
        _mask = OpenSimplex3MechanismData.Masks(9021);
    }

    [Benchmark(Baseline = true, Description = "Convert selected sign")]
    public float ConvertSelectedSign()
    {
        Vector<float> total = Vector<float>.Zero;
        for (int index = 0; index < _sign.Length; index++)
        {
            total += Vector.ConditionalSelect(
                Vector.AsVectorSingle(_mask[index]), Vector.ConvertToSingle(_sign[index]), Vector<float>.Zero);
        }

        return Vector.Sum(total);
    }

    [Benchmark(Description = "Flip selected unit sign")]
    public float FlipSelectedUnitSign()
    {
        Vector<float> total = Vector<float>.Zero;
        Vector<float> one = Vector<float>.One;
        for (int index = 0; index < _sign.Length; index++)
        {
            Vector<float> signedUnit = VectorOps.FlipSign(one, _sign[index]);
            total += Vector.ConditionalSelect(
                Vector.AsVectorSingle(_mask[index]), signedUnit, Vector<float>.Zero);
        }

        return Vector.Sum(total);
    }
}

/// <summary>H-012: remove a comparison made redundant by the preceding X-axis exclusion.</summary>
[NoiseBenchmark]
public class OpenSimplex3LeanSelection
{
    private Vector<float>[] _x = [];
    private Vector<float>[] _y = [];
    private Vector<float>[] _z = [];

    [GlobalSetup]
    public void Setup()
    {
        _x = OpenSimplex3MechanismData.Floats(9030, 0f, 0.5f);
        _y = OpenSimplex3MechanismData.Floats(9031, 0f, 0.5f);
        _z = OpenSimplex3MechanismData.Floats(9032, 0f, 0.5f);
    }

    [Benchmark(Baseline = true, Description = "Four comparisons")]
    public int FourComparisons()
    {
        Vector<int> total = Vector<int>.Zero;
        for (int index = 0; index < _x.Length; index++)
        {
            Vector<int> leanX = ~(Vector.AsVectorInt32(Vector.LessThan(_x[index], _y[index])))
                & ~(Vector.AsVectorInt32(Vector.LessThan(_x[index], _z[index])));
            Vector<int> leanY = ~leanX
                & Vector.AsVectorInt32(Vector.GreaterThan(_y[index], _x[index]))
                & ~(Vector.AsVectorInt32(Vector.LessThan(_y[index], _z[index])));
            Vector<int> leanZ = ~(leanX | leanY);
            total += leanX ^ leanY ^ leanZ;
        }

        return Vector.Sum(total);
    }

    [Benchmark(Description = "Three comparisons")]
    public int ThreeComparisons()
    {
        Vector<int> total = Vector<int>.Zero;
        for (int index = 0; index < _x.Length; index++)
        {
            Vector<int> leanX = ~(Vector.AsVectorInt32(Vector.LessThan(_x[index], _y[index])))
                & ~(Vector.AsVectorInt32(Vector.LessThan(_x[index], _z[index])));
            Vector<int> leanY = ~leanX
                & ~(Vector.AsVectorInt32(Vector.LessThan(_y[index], _z[index])));
            Vector<int> leanZ = ~(leanX | leanY);
            total += leanX ^ leanY ^ leanZ;
        }

        return Vector.Sum(total);
    }
}

/// <summary>H-013: clamp attenuation before the quartic rather than compare and blend afterward.</summary>
[NoiseBenchmark]
public class OpenSimplex3Attenuation
{
    private Vector<float>[] _attenuation = [];
    private Vector<float>[] _gradient = [];

    [GlobalSetup]
    public void Setup()
    {
        _attenuation = OpenSimplex3MechanismData.Floats(9040, -0.4f, 1f);
        _gradient = OpenSimplex3MechanismData.Floats(9041, -1f, 2f);
    }

    [Benchmark(Baseline = true, Description = "Compare and select")]
    public float CompareAndSelect()
    {
        Vector<float> total = Vector<float>.Zero;
        for (int index = 0; index < _attenuation.Length; index++)
        {
            Vector<float> t = _attenuation[index];
            Vector<float> t2 = t * t;
            Vector<float> contribution = (t2 * t2) * _gradient[index];
            total += Vector.ConditionalSelect(Vector.GreaterThan(t, Vector<float>.Zero), contribution, Vector<float>.Zero);
        }

        return Vector.Sum(total);
    }

    [Benchmark(Description = "Clamp before quartic")]
    public float ClampBeforeQuartic()
    {
        Vector<float> total = Vector<float>.Zero;
        for (int index = 0; index < _attenuation.Length; index++)
        {
            Vector<float> t = Vector.Max(_attenuation[index], Vector<float>.Zero);
            Vector<float> t2 = t * t;
            total += (t2 * t2) * _gradient[index];
        }

        return Vector.Sum(total);
    }
}
