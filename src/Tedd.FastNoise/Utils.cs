using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Tedd.FastNoise
{
    public static class Utils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SplitFraction(Vector128<double> d, out Vector128<double> i, out Vector128<double> f)
        {
            if (Avx2.IsSupported)
            {
                i = Avx2.RoundToZero(d);
                f = Avx2.Subtract(d, i);
            }
            else if (Avx.IsSupported)
            {
                i = Avx.RoundToZero(d);
                f = Avx.Subtract(d, i);
            }
            else if (Sse42.IsSupported)
            {
                i = Sse42.RoundToZero(d);
                f = Sse42.Subtract(d, i);
            }
            else
            {
                throw new Exception("Need Sse42, Avx or Avx2");
            }
        }
    }
}
