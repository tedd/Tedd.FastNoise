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
foreach (int width in new[] { 16, 17, 64 })
    foreach (int layers in new[] { 1, 4, 8 })
        registry.Register(new CompositeScenario(width, layers));
var host = new HarnessHost(registry, args[0], new InlineDispatcher(), null);
Console.WriteLine($"PID={Environment.ProcessId}");
await host.ListenAsync(CancellationToken.None);

sealed class CompositeScenario : ISynchronousScenario
{
    private readonly int _width, _layers;
    private readonly GridRegion3D _region;
    private readonly Original.GridRegion3D _originalRegion;
    private readonly float[] _output, _expected;
    private readonly CompiledNoiseStack _stack;
    private readonly Original.CompiledNoiseStack _original;
    private string _variant = "baseline";

    public CompositeScenario(int width, int layers)
    {
        _width = width; _layers = layers;
        _region = new(-137f, 87f, -23f, width, 64, 16, 1f);
        _originalRegion = new(-137f, 87f, -23f, width, 64, 16, 1f);
        _output = new float[_region.SampleCount]; _expected = new float[_output.Length];
        var stack = new NoiseStack { ParallelThreshold = 1 };
        var original = new Original.NoiseStack { ParallelThreshold = 1 };
        for (int i = 0; i < layers; i++)
        {
            float frequency = 0.002f * MathF.Pow(2f, i), amplitude = MathF.Pow(0.5f, i);
            stack.Add(new NoiseLayer { Source = new NoiseGenerator(1337 + i) { NoiseType = NoiseType.OpenSimplex2,
                FractalType = FractalType.FBm, Octaves = 4, Frequency = frequency }, Amplitude = amplitude });
            original.Add(new Original.NoiseLayer { Source = new Original.NoiseGenerator(1337 + i) { NoiseType = Original.NoiseType.OpenSimplex2,
                FractalType = Original.FractalType.FBm, Octaves = 4, Frequency = frequency }, Amplitude = amplitude });
        }
        _stack = stack.Compile(); _original = original.Compile();
    }
    public ScenarioDescriptor Describe() => new($"composite-3d-w{_width}-l{_layers}", "1",
        $"opensimplex2-fbm4-1337-origin-137-87-23-w{_width}-64-16-step1-l{_layers}",
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
