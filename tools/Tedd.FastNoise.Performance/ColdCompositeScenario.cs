// Charges construction and the first fill of a fresh compiled stack in an already-warm process.
// Output storage is reused; generated executors and resolved plans cannot survive between operations.
extern alias baseline;
using System;
using System.Collections.Generic;
using Tedd.DotNetLab.Contracts;
using Tedd.DotNetLab.Harness;
using Tedd.FastNoise;
using Original = baseline::Tedd.FastNoise;

sealed class ColdCompositeScenario : ISynchronousScenario
{
    private readonly GridRegion3D _region = new(-137f, 87f, -23f, 16, 64, 16, 1f);
    private readonly Original.GridRegion3D _originalRegion = new(-137f, 87f, -23f, 16, 64, 16, 1f);
    private readonly float[] _output = new float[16 * 64 * 16], _expected = new float[16 * 64 * 16];
    private string _variant = "baseline";
    public ScenarioDescriptor Describe() => new("composite-3d-first-fill-w16-l4", "1",
        "opensimplex2-fbm4-1337-origin-137-87-23-w16-64-16-step1-l4-new-stack-per-operation",
        ResetLevel.LogicalState, ThreadAffinity.Any, false, "one fresh stack construction and first complete 3D fill",
        new[] { "baseline", "candidate" }, Array.Empty<string>(), new Dictionary<string, int>());
    public void Prepare(ScenarioContext context)
    {
        _variant = context.Variant;
        OriginalStack().Fill(_expected, _originalRegion, Original.NoiseBackend.Simd);
    }
    public void Reset(ResetContext context) { }
    public long ExecuteBatch(int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
            if (_variant == "baseline") OriginalStack().Fill(_output, _originalRegion, Original.NoiseBackend.Simd);
            else CurrentStack().Fill(_output, _region, NoiseBackend.Simd);
        return operationCount;
    }
    public ValidationResult Validate()
    {
        for (int i = 0; i < _output.Length; i++)
            if (BitConverter.SingleToInt32Bits(_output[i]) != BitConverter.SingleToInt32Bits(_expected[i]))
                return new(false, $"First-fill sample {i} differs.");
        return new(true, "Every first-fill output matches original bits.");
    }
    public void Cleanup() { }
    private static CompiledNoiseStack CurrentStack()
    {
        var stack = new NoiseStack { Lod = LodPolicy.Disabled, ParallelThreshold = int.MaxValue };
        for (int i = 0; i < 4; i++)
            stack.Add(new NoiseLayer { Source = new NoiseGenerator(1337 + i)
            {
                NoiseType = NoiseType.OpenSimplex2, FractalType = FractalType.FBm, Octaves = 4,
                Frequency = .002f * MathF.Pow(2f, i),
            }, Amplitude = MathF.Pow(.5f, i) });
        return stack.Compile();
    }
    private static Original.CompiledNoiseStack OriginalStack()
    {
        var stack = new Original.NoiseStack { Lod = Original.LodPolicy.Disabled, ParallelThreshold = int.MaxValue };
        for (int i = 0; i < 4; i++)
            stack.Add(new Original.NoiseLayer { Source = new Original.NoiseGenerator(1337 + i)
            {
                NoiseType = Original.NoiseType.OpenSimplex2, FractalType = Original.FractalType.FBm, Octaves = 4,
                Frequency = .002f * MathF.Pow(2f, i),
            }, Amplitude = MathF.Pow(.5f, i) });
        return stack.Compile();
    }
}
