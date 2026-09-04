using System.Reflection;
using BenchmarkDotNet.Running;

namespace Tedd.FastNoise.Benchmark;

/// <summary>
/// Entry point. Run without arguments for the interactive picker, or pass a filter:
/// <c>dotnet run -c Release -- --filter *Heightmap2D*</c>.
/// </summary>
/// <remarks>
/// Every benchmark here exists to answer a specific question, and each class says which one at the
/// top. The rule for this project: no optimisation lands in the library without a benchmark that
/// showed it was worth landing, and the archived v1 implementation stays frozen in
/// <c>archive/v1</c> so the comparison has a fixed reference point.
/// </remarks>
public static class Program
{
    /// <summary>Runs the selected benchmarks.</summary>
    /// <param name="args">BenchmarkDotNet arguments, such as <c>--filter</c>.</param>
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}
