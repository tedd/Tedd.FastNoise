extern alias baseline;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tedd.DotNetLab.Contracts;
using Tedd.DotNetLab.Harness;
using Tedd.FastNoise;
using Original = baseline::Tedd.FastNoise;

var registry = new ScenarioRegistry();
foreach (int samples in new[] { 1, 256, 4096, 65536 })
    registry.Register(new EarthScenario(samples, true));
registry.Register(new EarthScenario(256, false));
foreach (int width in new[] { 16, 17, 64 })
    foreach (int layers in new[] { 1, 4, 8 })
        registry.Register(new CompositeScenario(width, layers));
foreach (int samples in new[] { 4096, 65536, 262144 })
    registry.Register(new GraphScenario(samples));
foreach (int width in new[] { 16, 64 })
    registry.Register(new CompositeScenario(width, 16, true));
bool owned = args[0] == "--harness";
var host = new HarnessHost(registry, args[owned ? 1 : 0], new InlineDispatcher(), null);
if (owned)
    await host.RunOwnedAsync();
else
{
    Console.WriteLine($"PID={Environment.ProcessId}");
    await host.ListenAsync(CancellationToken.None);
}

sealed class CompositeScenario : ISynchronousScenario
{
    private readonly int _width, _layers;
    private readonly GridRegion3D _region;
    private readonly Original.GridRegion3D _originalRegion;
    private readonly float[] _output, _expected;
    private readonly CompiledNoiseStack _stack;
    private readonly Original.CompiledNoiseStack _original;
    private readonly bool _replace;
    private string _variant = "baseline";

    public CompositeScenario(int width, int layers, bool replace = false)
    {
        _width = width; _layers = layers; _replace = replace;
        _region = new(-137f, 87f, -23f, width, 64, 16, 1f);
        _originalRegion = new(-137f, 87f, -23f, width, 64, 16, 1f);
        _output = new float[_region.SampleCount]; _expected = new float[_output.Length];
        var stack = new NoiseStack { ParallelThreshold = 1 };
        var original = new Original.NoiseStack { ParallelThreshold = 1 };
        for (int i = 0; i < layers; i++)
        {
            float frequency = 0.002f * MathF.Pow(2f, i), amplitude = MathF.Pow(0.5f, i);
            stack.Add(new NoiseLayer { Source = new NoiseGenerator(1337 + i) { NoiseType = NoiseType.OpenSimplex2,
                FractalType = FractalType.FBm, Octaves = 4, Frequency = frequency }, Amplitude = amplitude,
                Blend = replace && i == layers - 1 ? LayerBlend.Replace : LayerBlend.Add });
            original.Add(new Original.NoiseLayer { Source = new Original.NoiseGenerator(1337 + i) { NoiseType = Original.NoiseType.OpenSimplex2,
                FractalType = Original.FractalType.FBm, Octaves = 4, Frequency = frequency }, Amplitude = amplitude,
                Blend = replace && i == layers - 1 ? Original.LayerBlend.Replace : Original.LayerBlend.Add });
        }
        _stack = stack.Compile(); _original = original.Compile();
    }
    public ScenarioDescriptor Describe() => new($"composite-3d-w{_width}-l{_layers}{(_replace ? "-replace" : "")}", "1",
        $"opensimplex2-fbm4-1337-origin-137-87-23-w{_width}-64-16-step1-l{_layers}{(_replace ? "-replace" : "")}",
        ResetLevel.LogicalState, ThreadAffinity.Any, false, "one complete 3D composite volume",
        new[] { "baseline", "candidate", "baseline-parallel", "candidate-parallel" }, Array.Empty<string>(), new Dictionary<string, int>());
    public void Prepare(ScenarioContext context)
    {
        _variant = context.Variant;
        _original.Fill(_expected, _originalRegion, Original.NoiseBackend.Simd);
    }
    public void Reset(ResetContext context) { }
    public long ExecuteBatch(int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
            switch (_variant)
            {
                case "baseline": _original.Fill(_output, _originalRegion, Original.NoiseBackend.Simd); break;
                case "baseline-parallel": _original.Fill(_output, _originalRegion, Original.NoiseBackend.Parallel); break;
                case "candidate-parallel": _stack.Fill(_output, _region, NoiseBackend.Parallel); break;
                default: _stack.Fill(_output, _region, NoiseBackend.Simd); break;
            }
        return operationCount;
    }
    public ValidationResult Validate()
    {
        for (int i = 0; i < _output.Length; i++)
            if (BitConverter.SingleToInt32Bits(_output[i]) != BitConverter.SingleToInt32Bits(_expected[i]))
                return new(false, $"Sample {i} differs from original SIMD output.");
        return new(true, "All samples match the original SIMD output bit for bit.");
    }
    public void Cleanup() { }
}

sealed class GraphScenario : ISynchronousScenario
{
    private readonly int _samples;
    private readonly double[] _inputs, _output, _expected;
    private readonly Tedd.FastNoise.Procedural.CompiledProceduralGraph _graph;
    private readonly Original.Procedural.CompiledProceduralGraph _original;
    private string _variant = "baseline";

    public GraphScenario(int samples)
    {
        _samples = samples;
        _inputs = new double[samples * 3]; _output = new double[samples]; _expected = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            _inputs[3 * i] = -137f + i % 64;
            _inputs[3 * i + 1] = 87f + i / 64 % 64;
            _inputs[3 * i + 2] = -23f + i / 4096;
        }
        var graph = new Tedd.FastNoise.Procedural.ProceduralGraph();
        var original = new Original.Procedural.ProceduralGraph();
        var x = graph.Input(0, Tedd.FastNoise.Procedural.ProceduralType.Float32);
        var y = graph.Input(1, Tedd.FastNoise.Procedural.ProceduralType.Float32);
        var z = graph.Input(2, Tedd.FastNoise.Procedural.ProceduralType.Float32);
        var ox = original.Input(0, Original.Procedural.ProceduralType.Float32);
        var oy = original.Input(1, Original.Procedural.ProceduralType.Float32);
        var oz = original.Input(2, Original.Procedural.ProceduralType.Float32);
        var sum = graph.Constant(0f); var originalSum = original.Constant(0f);
        for (int i = 0; i < 4; i++)
        {
            float frequency = 0.002f * MathF.Pow(2f, i), amplitude = MathF.Pow(0.5f, i);
            sum += graph.Noise3D(new NoiseGenerator(1337 + i) { NoiseType = NoiseType.OpenSimplex2,
                FractalType = FractalType.FBm, Octaves = 4, Frequency = frequency }, x, y, z) * graph.Constant(amplitude);
            originalSum += original.Noise3D(new Original.NoiseGenerator(1337 + i) { NoiseType = Original.NoiseType.OpenSimplex2,
                FractalType = Original.FractalType.FBm, Octaves = 4, Frequency = frequency }, ox, oy, oz) * original.Constant(amplitude);
        }
        _graph = graph.Compile(sum); _original = original.Compile(originalSum);
    }
    public ScenarioDescriptor Describe() => new($"graph-3d-n{_samples}", "1",
        $"opensimplex2-fbm4-l4-1337-xyz-interleaved-n{_samples}", ResetLevel.LogicalState, ThreadAffinity.Any, false,
        "one complete batch of 3D composite records", new[] { "baseline", "candidate", "baseline-parallel", "candidate-parallel" },
        Array.Empty<string>(), new Dictionary<string, int>());
    public void Prepare(ScenarioContext context)
    {
        _variant = context.Variant;
        _original.Fill(_inputs, _expected, _samples, Original.NoiseBackend.Simd);
    }
    public void Reset(ResetContext context) { }
    public long ExecuteBatch(int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
            switch (_variant)
            {
                case "baseline": _original.Fill(_inputs, _output, _samples, Original.NoiseBackend.Simd); break;
                case "baseline-parallel": _original.Fill(_inputs, _output, _samples, Original.NoiseBackend.Parallel); break;
                case "candidate-parallel": _graph.Fill(_inputs, _output, _samples, NoiseBackend.Parallel); break;
                default: _graph.Fill(_inputs, _output, _samples, NoiseBackend.Simd); break;
            }
        return operationCount;
    }
    public ValidationResult Validate()
    {
        for (int i = 0; i < _samples; i++)
            if (BitConverter.DoubleToInt64Bits(_output[i]) != BitConverter.DoubleToInt64Bits(_expected[i]))
                return new(false, $"Record {i} differs from original SIMD output.");
        return new(true, "All records match original SIMD output bit for bit.");
    }
    public void Cleanup() { }
}
