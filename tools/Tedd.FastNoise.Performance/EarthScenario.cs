// Measures the Forcecraft Earth recipe with immutable datum-surface inputs and exact output validation.
// Construction and oracle generation occur outside timing; one operation writes the complete batch.
extern alias baseline;
using System;
using System.Collections.Generic;
using Tedd.DotNetLab.Contracts;
using Tedd.DotNetLab.Harness;
using Tedd.FastNoise;
using Current = Tedd.FastNoise.Performance.CurrentRecipe;
using Original = Tedd.FastNoise.Performance.BaselineRecipe;

sealed class EarthScenario : ISynchronousScenario
{
    private readonly int _samples, _outputs;
    private readonly bool _land;
    private readonly double[] _inputs, _output, _expected;
    private readonly Tedd.FastNoise.Procedural.CompiledProceduralGraph _graph;
    private readonly baseline::Tedd.FastNoise.Procedural.CompiledProceduralGraph _original;
    private string _variant = "baseline";

    public EarthScenario(int samples, bool land, double step = 1d)
    {
        _samples = samples; _land = land; _outputs = land ? 11 : 9;
        _inputs = new double[samples * 3]; _output = new double[samples * _outputs]; _expected = new double[_output.Length];
        // Consecutive 16x16 columns on signed cube faces, with planet datum radius 500000.
        // Fractional, negative and large tangent origins exercise the established float conversion.
        for (int i = 0; i < samples; i++)
        {
            int column = i / 256, axis = column / 2 % 3;
            double u = -17345.25 + column * 16 + i % 16;
            double v = 29377.75 + i % 256 / 16;
            _inputs[3 * i + axis] = (column % 2 == 0 ? 1 : -1) * 500000d;
            _inputs[3 * i + (axis + 1) % 3] = u;
            _inputs[3 * i + (axis + 2) % 3] = v;
        }
        _graph = land ? Current.EarthRecipe.CreateLand(1337, new Current.RegionServerConfig(), step)
            : Current.EarthRecipe.CreateNoise(1337, new Current.ReliefOptions(), step);
        _original = land ? Original.EarthRecipe.CreateLand(1337, new Original.RegionServerConfig(), step)
            : Original.EarthRecipe.CreateNoise(1337, new Original.ReliefOptions(), step);
    }

    // Pure correctness mode also supplies a reproducible generated-code inspection workload.
    public void Check()
    {
        _original.Fill(_inputs, _expected, _samples);
        foreach (string variant in new[] { "candidate", "candidate-scalar", "candidate-parallel" })
        {
            _variant = variant;
            ExecuteBatch(1);
            var validation = Validate();
            if (!validation.Correct) throw new InvalidOperationException(validation.ToString());
        }
        Span<double> point = stackalloc double[_outputs];
        for (int i = 0; i < _samples; i++)
        {
            _graph.Evaluate(_inputs.AsSpan(3 * i, 3), point);
            for (int j = 0; j < _outputs; j++)
                if (BitConverter.DoubleToInt64Bits(point[j]) != BitConverter.DoubleToInt64Bits(_expected[i * _outputs + j]))
                    throw new InvalidOperationException($"Earth point {i}, output {j} differs.");
        }
    }

    public ScenarioDescriptor Describe() => new($"earth-{(_land ? "land" : "noise")}-n{_samples}", "1",
        $"earth-a78ba56e-defaults-seed1337-step1-sixfaces-n{_samples}-v1", ResetLevel.LogicalState, ThreadAffinity.Any,
        false, _samples == 1 ? "one complete Earth point evaluation" : "one complete Earth datum batch",
        new[] { "baseline", "candidate", "baseline-scalar", "candidate-scalar", "baseline-parallel", "candidate-parallel" },
        Array.Empty<string>(), new Dictionary<string, int>());

    public void Prepare(ScenarioContext context)
    {
        _variant = context.Variant;
        if (_samples == 1) _original.Evaluate(_inputs, _expected);
        else _original.Fill(_inputs, _expected, _samples);
    }
    public void Reset(ResetContext context) { }
    public long ExecuteBatch(int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            if (_variant.StartsWith("baseline", StringComparison.Ordinal))
            {
                if (_samples == 1) _original.Evaluate(_inputs, _output);
                else _original.Fill(_inputs, _output, _samples, _variant switch
                {
                    "baseline-scalar" => baseline::Tedd.FastNoise.NoiseBackend.Scalar,
                    "baseline-parallel" => baseline::Tedd.FastNoise.NoiseBackend.Parallel,
                    _ => baseline::Tedd.FastNoise.NoiseBackend.Auto
                });
            }
            else
            {
                if (_samples == 1) _graph.Evaluate(_inputs, _output);
                else _graph.Fill(_inputs, _output, _samples, _variant switch
                {
                    "candidate-scalar" => NoiseBackend.Scalar,
                    "candidate-parallel" => NoiseBackend.Parallel,
                    _ => NoiseBackend.Auto
                });
            }
        }
        return operationCount;
    }
    public ValidationResult Validate()
    {
        for (int i = 0; i < _output.Length; i++)
            if (BitConverter.DoubleToInt64Bits(_output[i]) != BitConverter.DoubleToInt64Bits(_expected[i]))
                return new(false, $"Earth output {i} differs from original bits.");
        return new(true, "Every Earth field, elevation and valley matches original bits.");
    }
    public void Cleanup() { }
}
