using System.Collections.Generic;
using System.Numerics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tedd.FastNoise.Benchmark.Benchmarks
{
    [Config(typeof(TestConfig))]
    public class FadeMethods
    {
        private const int Iterations = 100_000;

        [GlobalSetup]
        public void Setup()
        {
        }

        public double Faded;
        public Vector<double> FadedVector;

        [Benchmark()]
        public void flafla2()
        {
            // Original fade method
            Faded = 0d;
            var g = new Generator(0, 3);
            double t = 0;
            for (var i = 0; i < Iterations; i++)
            {
                t += 0.03d;
                Faded += t * t * t * (t * (t * 6 - 15) + 10);
            }
        }

        [Benchmark()]
        public void Vector1()
        {
            // Original fade method
            FadedVector = Vector<double>.Zero;
            var g = new Generator(0, 3);
            Vector<double> t = Vector<double>.Zero;
            var add = new Vector<double>(0.03d);
            Vector<double> i = Vector<double>.Zero;
            Vector<double> iv = new Vector<double>(Iterations);
            var v6 = new Vector<double>(6d);
            var v10 = new Vector<double>(10d);
            var v15 = new Vector<double>(15d);
            for (; Vector.LessThan(iv,i)== Vector<long>.Zero; i+= Vector<double>.One)
            {
                Vector.Add(t, add);

                FadedVector = t * t * t * (t * (t * v6 - v15) + v10);

            }
        }
    }
}
