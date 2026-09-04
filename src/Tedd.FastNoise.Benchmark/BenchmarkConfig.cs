using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;

namespace Tedd.FastNoise.Benchmark;

/// <summary>
/// Shared configuration: one job, ratios against a declared baseline, and a throughput column in
/// samples per second.
/// </summary>
/// <remarks>
/// Ratio to baseline is the number that matters here. Absolute nanoseconds are a property of the
/// machine that ran them and age badly in a README; "3.9x the scalar path" survives a hardware
/// refresh.
/// </remarks>
public sealed class BenchmarkConfig : ManualConfig
{
    /// <summary>Builds the configuration.</summary>
    public BenchmarkConfig()
    {
        AddLogger(ConsoleLogger.Default);
        AddJob(Job.Default.WithId("net10"));

        AddColumnProvider(DefaultColumnProviders.Instance);
        AddColumn(BaselineRatioColumn.RatioMean);
        AddColumn(RankColumn.Arabic);

        AddExporter(MarkdownExporter.GitHub);
        AddExporter(CsvExporter.Default);

        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
        WithOptions(ConfigOptions.DisableLogFile);
    }
}

/// <summary>Applies <see cref="BenchmarkConfig"/> and reports allocations.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoiseBenchmarkAttribute : Attribute, IConfigSource
{
    /// <summary>Builds the attribute.</summary>
    public NoiseBenchmarkAttribute() => Config = new BenchmarkConfig();

    /// <inheritdoc />
    public IConfig Config { get; }
}
