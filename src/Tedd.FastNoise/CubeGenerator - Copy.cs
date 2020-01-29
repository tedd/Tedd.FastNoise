using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using c = Tedd.FastNoiseTmp.Constants;

namespace Tedd.FastNoiseTmp
{
    public class Generator
    {

        // Borrowing from https://flafla2.github.io/2014/08/09/perlinnoise.html which is based on https://mrl.nyu.edu/~perlin/noise/

        public Generator(int seed, int dimensions)
        {

        }

        public void Fill(Span<byte> cube, int sides)
        {

        }

        //public double Perlin(double x, double y, double z)
        //{

        //    int xi = (int) x & 255; // Calculate the "unit cube" that the point asked will be located in
        //    int yi = (int) y & 255; // The left bound is ( |_x_|,|_y_|,|_z_| ) and the right bound is that
        //    int zi = (int) z & 255; // plus 1.  Next we calculate the location (from 0.0 to 1.0) in that cube.
        //    double xf = x - (int) x; // We also fade the location to smooth the result.
        //    double yf = y - (int) y;
        //    double zf = z - (int) z;
        //    var u = fade(xf);
        //    var v = fade(yf);
        //    var w = fade(zf);

        //    int aaa, aba, aab, abb, baa, bba, bab, bbb;
        //    aaa =  c.PO[c.PO[c.PO[xi] + yi] + zi];
        //    aba = c.PO[c.PO[c.PO[xi] + inc(yi)] + zi];
        //    aab = c.PO[c.PO[c.PO[xi] + yi] + inc(zi)];
        //    abb = c.PO[c.PO[c.PO[xi] + inc(yi)] + inc(zi)];
        //    baa = c.PO[c.PO[c.PO[inc(xi)] + yi] + zi];
        //    bba = c.PO[c.PO[c.PO[inc(xi)] + inc(yi)] + zi];
        //    bab = c.PO[c.PO[c.PO[inc(xi)] + yi] + inc(zi)];
        //    bbb = c.PO[c.PO[c.PO[inc(xi)] + inc(yi)] + inc(zi)];

        //    double x1, x2, y1, y2;
        //    x1 = lerp(grad(aaa, xf, yf, zf), // The gradient function calculates the dot product between a pseudorandom
        //        grad(baa, xf - 1, yf, zf), // gradient vector and the vector from the input coordinate to the 8
        //        u); // surrounding points in its unit cube.
        //    x2 = lerp(
        //        grad(aba, xf, yf - 1,
        //            zf), // This is all then lerped together as a sort of weighted average based on the faded (u,v,w)
        //        grad(bba, xf - 1, yf - 1, zf), // values we made earlier.
        //        u);
        //    y1 = lerp(x1, x2, v);

        //    x1 = lerp(grad(aab, xf, yf, zf - 1),
        //        grad(bab, xf - 1, yf, zf - 1),
        //        u);
        //    x2 = lerp(grad(abb, xf, yf - 1, zf - 1),
        //        grad(bbb, xf - 1, yf - 1, zf - 1),
        //        u);
        //    y2 = lerp(x1, x2, v);

        //    return
        //        (lerp(y1, y2, w) + 1) /
        //        2; // For convenience we bound it to 0 - 1 (theoretical min/max before is -1 - 1)
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte inc(byte num) => num++;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> grad(int hash, Vector<double> x, Vector<double> y, Vector<double> z)
        {
            int h = hash & 15; // Take the hashed value and take the first 4 bits of it (15 == 0b1111)
            var
                u = h < 8 /* 0b1000 */
                    ? x
                    : y; // If the most significant bit (MSB) of the hash is 0 then set u = x.  Otherwise y.

            Vector<double> v; // In Ken Perlin's original implementation this was another conditional operator (?:).  I
            // expanded it for readability.

            if (h < 4 /* 0b0100 */) // If the first and second significant bits are 0 set v = y
                v = y;
            else if (h == 12 /* 0b1100 */ || h == 14 /* 0b1110*/
            ) // If the first and second significant bits are 1 set v = x
                v = x;
            else // If the first and second significant bits are not equal (0/1, 1/0) set v = z
                v = z;

            return
                ((h & 1) == 0 ? u : -u) +
                ((h & 2) == 0
                    ? v
                    : -v
                ); // Use the last 2 bits to decide if u and v are positive or negative.  Then return their addition.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> fade(Vector<double> t)
        {
            // Fade function as defined by Ken Perlin.  This eases coordinate values
            // so that they will "ease" towards integral values.  This ends up smoothing
            // the final output.
            return t * t * t * (t * (t * new Vector<double>(6) - new Vector<double>(15)) + new Vector<double>(10)); // 6t^5 - 15t^4 + 10t^3
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> lerp(Vector<double> a, Vector<double> b, Vector<double> x)
        {
            return a + x * (b - a);
        }


        public void Fill(in Span<double> span, in int sizeX, in int sizeY)
        {
            var vsx = new Vector<double>(sizeX);
            var vsy = new Vector<double>(sizeY);
            var fx = new Vector<double>(1d/sizeX);
            var fy = new Vector<double>(1d / sizeY);
            var vector = new Vector<double>(span);
            var cx = Vector<double>.Zero;
            var cy = Vector<double>.Zero;

            var cz = Vector<double>.Zero;
            var z = Vector<double>.Zero;
            for (; Vector.LessThanOrEqual(vsx, cx) == Vector<long>.Zero; cx += Vector<double>.One)
            {
                var x = fx * cx;
                for (; Vector.LessThanOrEqual(vsy, cy) == Vector<long>.Zero; cy += Vector<double>.One)
                {
                    var y = fy * cy;
                    var xi = (byte)Vector.ConvertToInt64(x)[0];
                    var yi = (byte)Vector.ConvertToInt64(y)[0];
                    var zi = (byte)Vector.ConvertToInt64(z)[0];
                    var u = x;
                    var v = y;
                    var w = z;
                    var xf = x-Vector.ConvertToDouble(Vector.ConvertToInt64(x));
                    var yf = y - Vector.ConvertToDouble(Vector.ConvertToInt64(y));
                    var zf = z - Vector.ConvertToDouble(Vector.ConvertToInt64(z));

                    //		int xi = (int)x & 255;                              // Calculate the "unit cube" that the point asked will be located in
                    //int yi = (int)y & 255;                              // The left bound is ( |_x_|,|_y_|,|_z_| ) and the right bound is that
                    //int zi = (int)z & 255;                              // plus 1.  Next we calculate the location (from 0.0 to 1.0) in that cube.
                    //double xf = x - (int)x;                             // We also fade the location to smooth the result.
                    //double yf = y - (int)y;
                    //double zf = z - (int)z;
                    //double u = fade(xf);
                    //double v = fade(yf);
                    //double w = fade(zf);

                    int aaa, aba, aab, abb, baa, bba, bab, bbb;
                    aaa = c.PO[c.PO[c.PO[xi] + yi] + zi];
                    aba = c.PO[c.PO[c.PO[xi] + inc(yi)] + zi];
                    aab = c.PO[c.PO[c.PO[xi] + yi] + inc(zi)];
                    abb = c.PO[c.PO[c.PO[xi] + inc(yi)] + inc(zi)];
                    baa = c.PO[c.PO[c.PO[inc(xi)] + yi] + zi];
                    bba = c.PO[c.PO[c.PO[inc(xi)] + inc(yi)] + zi];
                    bab = c.PO[c.PO[c.PO[inc(xi)] + yi] + inc(zi)];
                    bbb = c.PO[c.PO[c.PO[inc(xi)] + inc(yi)] + inc(zi)];

                    Vector<double> x1, x2, y1, y2;
                    x1 = lerp(
                        grad(aaa, xf, yf,
                            zf), // The gradient function calculates the dot product between a pseudorandom
                        grad(baa, xf - Vector<double>.One, yf, zf), // gradient vector and the vector from the input coordinate to the 8
                        u); // surrounding points in its unit cube.
                    x2 = lerp(
                        grad(aba, xf, yf - Vector<double>.One,
                            zf), // This is all then lerped together as a sort of weighted average based on the faded (u,v,w)
                        grad(bba, xf - Vector<double>.One, yf - Vector<double>.One, zf), // values we made earlier.
                        u);
                    y1 = lerp(x1, x2, v);

                    x1 = lerp(grad(aab, xf, yf, zf - Vector<double>.One),
                        grad(bab, xf - Vector<double>.One, yf, zf - Vector<double>.One),
                        u);
                    x2 = lerp(grad(abb, xf, yf - Vector<double>.One, zf - Vector<double>.One),
                        grad(bbb, xf - Vector<double>.One, yf - Vector<double>.One, zf - Vector<double>.One),
                        u);
                    y2 = lerp(x1, x2, v);


                    var p = x + y * vsx;

                    var result =
                        (lerp(y1, y2, w) + Vector<double>.One) /
                        new Vector<double>(2); // For convenience we bound it to 0 - 1 (theoretical min/max before is -1 - 1)

                    span[(int)p[0]] = result[0];

                }
            }
        }
    }
}