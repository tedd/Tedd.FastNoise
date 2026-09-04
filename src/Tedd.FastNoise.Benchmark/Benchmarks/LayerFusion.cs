using System;
using BenchmarkDotNet.Attributes;

namespace Tedd.FastNoise.Benchmark.Benchmarks;

/// <summary>
/// Does fusing layers into one pass actually beat filling a buffer per layer and combining them?
/// </summary>
/// <remarks>
/// <para>
/// The hypothesis behind <see cref="CompiledNoiseStack"/>: with N layers, the obvious approach
/// writes N full buffers and then reads all of them back, while the fused loop keeps the
/// accumulator in a register and writes once. If the hypothesis is right the gap widens with layer
/// count and with output size, because the naive version is limited by memory traffic that the
/// fused version does not generate.
/// </para>
/// <para>
/// <see cref="NaiveSequential"/> is deliberately the code a competent user would write with only
/// <see cref="NoiseGenerator"/> available. It is not a straw man: it uses the same SIMD fills, it
/// just combines them afterwards.
/// </para>
/// </remarks>
[NoiseBenchmark]
[MemoryDiagnoser]
public class LayerFusion
{
    private NoiseGenerator[] _sources = [];
    private float[][] _scratch = [];
    private float[] _destination = [];
    private CompiledNoiseStack _fused = null!;
    private GridRegion2D _region;

    /// <summary>How many layers are stacked. A real world generator sits at the upper end.</summary>
    [Params(2, 4, 8)]
    public int Layers { get; set; }

    /// <summary>Edge length of the square output.</summary>
    [Params(512)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _region = new GridRegion2D(0f, 0f, Size, Size);
        _destination = new float[_region.SampleCount];

        _sources = new NoiseGenerator[Layers];
        _scratch = new float[Layers][];

        NoiseStack stack = new() { ParallelThreshold = 1 };

        for (int index = 0; index < Layers; index++)
        {
            // Each layer an octave apart, as a real terrain stack would be.
            NoiseGenerator source = new(1337 + index)
            {
                NoiseType = NoiseType.OpenSimplex2,
                FractalType = FractalType.FBm,
                Octaves = 3,
                Frequency = 0.002f * MathF.Pow(2f, index),
                ParallelThreshold = int.MaxValue,
            };

            _sources[index] = source;
            _scratch[index] = new float[_region.SampleCount];
            stack.Add(new NoiseLayer { Source = source, Amplitude = MathF.Pow(0.5f, index) });
        }

        _fused = stack.Compile();
    }

    /// <summary>Fill a buffer per layer, then walk the buffers combining them.</summary>
    [Benchmark(Baseline = true, Description = "Buffer per layer, then combine")]
    public float[] NaiveSequential()
    {
        for (int index = 0; index < Layers; index++)
        {
            _sources[index].Fill(_scratch[index], _region, NoiseBackend.Simd);
        }

        Span<float> destination = _destination;
        _scratch[0].CopyTo(destination);

        for (int index = 1; index < Layers; index++)
        {
            ReadOnlySpan<float> layer = _scratch[index];
            float amplitude = MathF.Pow(0.5f, index);

            for (int sample = 0; sample < destination.Length; sample++)
            {
                destination[sample] += layer[sample] * amplitude;
            }
        }

        return _destination;
    }

    /// <summary>Run every layer against coordinates held in registers, and write once.</summary>
    [Benchmark(Description = "Fused compiled stack")]
    public float[] Fused()
    {
        _fused.Fill(_destination, _region, NoiseBackend.Simd);
        return _destination;
    }

    /// <summary>The fused loop across all cores, which is what production would actually run.</summary>
    [Benchmark(Description = "Fused compiled stack, parallel")]
    public float[] FusedParallel()
    {
        _fused.Fill(_destination, _region, NoiseBackend.Parallel);
        return _destination;
    }
}
