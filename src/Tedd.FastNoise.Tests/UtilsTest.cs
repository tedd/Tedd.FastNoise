using System;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using Microsoft.VisualBasic.CompilerServices;
using Xunit;

namespace Tedd.FastNoise.Tests
{
    public class UtilsTest
    {
        private const int Iterations = 100_000;
        [Fact]
        public void SplitFraction()
        {
            var rnd = new Random();
            for (var n = 0; n < Iterations; n++)
            {
                var d = rnd.NextDouble() + rnd.Next(0, Int32.MaxValue - 1);
                var vd = Vector128.CreateScalar(d);
                Utils.SplitFraction(vd, out var vi, out var vf);

                var mi = (double)(int)d;
                var mf = d - mi;

                Assert.Equal(mi, vi.ToScalar());
                Assert.Equal(mf, vf.ToScalar());
            }
        }
    }
}
