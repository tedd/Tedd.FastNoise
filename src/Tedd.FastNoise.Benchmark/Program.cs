using System;
using BenchmarkDotNet.Running;
using Tedd.FastNoise.Benchmark.Benchmarks;

namespace Tedd.FastNoise.Benchmark
{
    class Program
    {
        static void Main(string[] args)
        {

            var faded = new FadeMethods();
            faded.Setup();
            faded.flafla2();
            faded.Vector1();
            var image = new ImageFill();
            image.GlobalSetup();
            image.IterationSetup();
            image.OriginalGenerator();
            image.NewGenerator();



            //var summary1 = BenchmarkRunner.Run<FadeMethods>();
            var summary2 = BenchmarkRunner.Run<ImageFill>();
        }
    }
}
