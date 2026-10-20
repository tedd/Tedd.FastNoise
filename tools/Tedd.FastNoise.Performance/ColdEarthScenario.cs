// Includes graph compilation and first Auto fill in a warm process, with reusable output buffers.
extern alias baseline;
using System;
using System.Collections.Generic;
using Tedd.DotNetLab.Contracts;
using Tedd.DotNetLab.Harness;
using Current = Tedd.FastNoise.Performance.CurrentRecipe;
using Original = Tedd.FastNoise.Performance.BaselineRecipe;

sealed class ColdEarthScenario : ISynchronousScenario
{
    private readonly bool _land;
    private readonly double[] _inputs = new double[256 * 3], _output, _expected;
    private string _variant = "baseline";
    public ColdEarthScenario(bool land)
    {
        _land = land;
        _output = new double[256 * (land ? 11 : 9)]; _expected = new double[_output.Length];
        for (int i = 0; i < 256; i++)
        {
            _inputs[3 * i] = 500000d;
            _inputs[3 * i + 1] = -17345.25 + i % 16;
            _inputs[3 * i + 2] = 29377.75 + i / 16;
        }
    }
    public ScenarioDescriptor Describe() => new($"earth-{(_land ? "land" : "noise")}-first-fill-n256", "1",
        "earth-a78ba56e-defaults-seed1337-step1-sixfaces-n256-v1-new-graph-per-operation",
        ResetLevel.LogicalState, ThreadAffinity.Any, false, "one fresh Earth graph compilation and first complete 256-record Auto fill",
        new[] { "baseline", "candidate", "entry-a", "entry-b" }, Array.Empty<string>(), new Dictionary<string, int>());
    public void Prepare(ScenarioContext context)
    {
        _variant = context.Variant.StartsWith("entry-", StringComparison.Ordinal) ? "candidate" : context.Variant;
        FillOriginal(_expected);
    }
    public void Reset(ResetContext context) { }
    public long ExecuteBatch(int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
            if (_variant == "baseline") FillOriginal(_output);
            else if (_land) Current.EarthRecipe.CreateLand(1337, new Current.RegionServerConfig(), 1d).Fill(_inputs, _output, 256);
            else Current.EarthRecipe.CreateNoise(1337, new Current.ReliefOptions(), 1d).Fill(_inputs, _output, 256);
        return operationCount;
    }
    private void FillOriginal(double[] output)
    {
        if (_land) Original.EarthRecipe.CreateLand(1337, new Original.RegionServerConfig(), 1d).Fill(_inputs, output, 256);
        else Original.EarthRecipe.CreateNoise(1337, new Original.ReliefOptions(), 1d).Fill(_inputs, output, 256);
    }
    public ValidationResult Validate()
    {
        for (int i = 0; i < _output.Length; i++)
            if (BitConverter.DoubleToInt64Bits(_output[i]) != BitConverter.DoubleToInt64Bits(_expected[i]))
                return new(false, $"Cold Earth output {i} differs from released bits.");
        return new(true, "Every cold Earth output matches released bits.");
    }
    public void Check()
    {
        FillOriginal(_expected);
        foreach (string variant in new[] { "baseline", "candidate" })
        {
            _variant = variant; ExecuteBatch(1);
            if (!Validate().Correct) throw new InvalidOperationException(Validate().ToString());
        }
    }
    public void Cleanup() { }
}
