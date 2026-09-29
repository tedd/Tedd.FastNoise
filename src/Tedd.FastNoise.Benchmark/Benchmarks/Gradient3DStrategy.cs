using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Tedd.FastNoise.Internal;

namespace Tedd.FastNoise.Benchmark.Benchmarks;

/// <summary>
/// Does encoding the restricted 3D gradient set reduce table traffic relative to three float gathers?
/// </summary>
[NoiseBenchmark]
public class Gradient3DStrategy
{
    private const int Vectors = 100_000;

    private Vector<int>[] _seeds = [];
    private Vector<int>[] _xPrimed = [];
    private Vector<int>[] _yPrimed = [];
    private Vector<int>[] _zPrimed = [];
    private Vector<float>[] _xd = [];
    private Vector<float>[] _yd = [];
    private Vector<float>[] _zd = [];

    [GlobalSetup]
    public void Setup()
    {
        Random random = new(1234);
        _seeds = new Vector<int>[Vectors];
        _xPrimed = new Vector<int>[Vectors];
        _yPrimed = new Vector<int>[Vectors];
        _zPrimed = new Vector<int>[Vectors];
        _xd = new Vector<float>[Vectors];
        _yd = new Vector<float>[Vectors];
        _zd = new Vector<float>[Vectors];

        int[] integers = new int[Vector<int>.Count];
        float[] floats = new float[Vector<float>.Count];

        for (int index = 0; index < Vectors; index++)
        {
            FillIntegers(random, integers);
            _seeds[index] = new Vector<int>(integers);
            FillIntegers(random, integers);
            _xPrimed[index] = new Vector<int>(integers);
            FillIntegers(random, integers);
            _yPrimed[index] = new Vector<int>(integers);
            FillIntegers(random, integers);
            _zPrimed[index] = new Vector<int>(integers);

            FillFloats(random, floats);
            _xd[index] = new Vector<float>(floats);
            FillFloats(random, floats);
            _yd[index] = new Vector<float>(floats);
            FillFloats(random, floats);
            _zd[index] = new Vector<float>(floats);
        }
    }

    [Benchmark(Baseline = true, Description = "Three float gathers")]
    public float ThreeFloatGathers()
    {
        Vector<float> total = Vector<float>.Zero;
        for (int index = 0; index < Vectors; index++)
        {
            total += Baseline(
                _seeds[index], _xPrimed[index], _yPrimed[index], _zPrimed[index],
                _xd[index], _yd[index], _zd[index]);
        }

        return Vector.Sum(total);
    }

    [Benchmark(Description = "One packed-code gather")]
    public float PackedCodeGather()
    {
        Vector<float> total = Vector<float>.Zero;
        for (int index = 0; index < Vectors; index++)
        {
            total += Hashing.GradCoord3<VectorOps, Vector<float>, Vector<int>>(
                _seeds[index], _xPrimed[index], _yPrimed[index], _zPrimed[index],
                _xd[index], _yd[index], _zd[index]);
        }

        return Vector.Sum(total);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> Baseline(
        Vector<int> seed,
        Vector<int> xPrimed,
        Vector<int> yPrimed,
        Vector<int> zPrimed,
        Vector<float> xd,
        Vector<float> yd,
        Vector<float> zd)
    {
        Vector<int> hash = Hashing.Hash3<VectorOps, Vector<float>, Vector<int>>(seed, xPrimed, yPrimed, zPrimed);
        hash ^= Vector.ShiftRightArithmetic(hash, 15);
        hash &= new Vector<int>(63 << 2);

        Vector<float> xg = VectorOps.Gather(Tables.Gradients3D, hash);
        Vector<float> yg = VectorOps.Gather(Tables.Gradients3D, hash | Vector<int>.One);
        Vector<float> zg = VectorOps.Gather(Tables.Gradients3D, hash | new Vector<int>(2));

        return ((xd * xg) + (yd * yg)) + (zd * zg);
    }

    private static void FillIntegers(Random random, int[] destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = random.Next(int.MinValue, int.MaxValue);
        }
    }

    private static void FillFloats(Random random, float[] destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = random.NextSingle() - 0.5f;
        }
    }
}
