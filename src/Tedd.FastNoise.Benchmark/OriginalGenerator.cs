using System;
using System.Runtime.CompilerServices;
using c= Tedd.FastNoise.Benchmark.Constants;

namespace Tedd.FastNoise.Benchmark
{
        internal static class Constants
        {

            /// <summary>
            /// Hash lookup table as defined by Ken Perlin.  This is a randomly arranged array of all numbers from 0-255 inclusive.
            /// </summary>
#if !NETCOREAPP2 && !NETSTANDARD && !DOTNET
            // https://github.com/dotnet/roslyn/pull/24621
            public static ReadOnlySpan<byte> Permutations => new byte[]
#else
        public static readonly byte[] Permutations =
#endif
        {
            151, 160, 137, 91, 90, 15,
            131, 13, 201, 95, 96, 53, 194, 233, 7, 225, 140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23,
            190, 6, 148, 247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32, 57, 177, 33,
            88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175, 74, 165, 71, 134, 139, 48, 27, 166,
            77, 146, 158, 231, 83, 111, 229, 122, 60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244,
            102, 143, 54, 65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169, 200, 196,
            135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64, 52, 217, 226, 250, 124, 123,
            5, 202, 38, 147, 118, 126, 255, 82, 85, 212, 207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42,
            223, 183, 170, 213, 119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
            129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104, 218, 246, 97, 228,
            251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241, 81, 51, 145, 235, 249, 14, 239, 107,
            49, 192, 214, 31, 181, 199, 106, 157, 184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254,
            138, 236, 205, 93, 222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180
        };

#if !NETCOREAPP2 && !NETSTANDARD && !DOTNET
            // https://github.com/dotnet/roslyn/pull/24621
            public static ReadOnlySpan<byte> PO => new byte[]
#else
        public static readonly byte[] PO =
#endif
        {
            151, 160, 137, 091, 090, 015, 131, 013, 201, 095, 096, 053, 194, 233, 007, 225,
            140, 036, 103, 030, 069, 142, 008, 099, 037, 240, 021, 010, 023, 190, 006, 148,
            247, 120, 234, 075, 000, 026, 197, 062, 094, 252, 219, 203, 117, 035, 011, 032,
            057, 177, 033, 088, 237, 149, 056, 087, 174, 020, 125, 136, 171, 168, 068, 175,
            074, 165, 071, 134, 139, 048, 027, 166, 077, 146, 158, 231, 083, 111, 229, 122,
            060, 211, 133, 230, 220, 105, 092, 041, 055, 046, 245, 040, 244, 102, 143, 054,
            065, 025, 063, 161, 001, 216, 080, 073, 209, 076, 132, 187, 208, 089, 018, 169,
            200, 196, 135, 130, 116, 188, 159, 086, 164, 100, 109, 198, 173, 186, 003, 064,
            052, 217, 226, 250, 124, 123, 005, 202, 038, 147, 118, 126, 255, 082, 085, 212,
            207, 206, 059, 227, 047, 016, 058, 017, 182, 189, 028, 042, 223, 183, 170, 213,
            119, 248, 152, 002, 044, 154, 163, 070, 221, 153, 101, 155, 167, 043, 172, 009,
            129, 022, 039, 253, 019, 098, 108, 110, 079, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 097, 228, 251, 034, 242, 193, 238, 210, 144, 012, 191, 179, 162, 241,
            081, 051, 145, 235, 249, 014, 239, 107, 049, 192, 214, 031, 181, 199, 106, 157,
            184, 084, 204, 176, 115, 121, 050, 045, 127, 004, 150, 254, 138, 236, 205, 093,
            222, 114, 067, 029, 024, 072, 243, 141, 128, 195, 078, 066, 215, 061, 156, 180,
            151, 160, 137, 091, 090, 015, 131, 013, 201, 095, 096, 053, 194, 233, 007, 225,
            140, 036, 103, 030, 069, 142, 008, 099, 037, 240, 021, 010, 023, 190, 006, 148,
            247, 120, 234, 075, 000, 026, 197, 062, 094, 252, 219, 203, 117, 035, 011, 032,
            057, 177, 033, 088, 237, 149, 056, 087, 174, 020, 125, 136, 171, 168, 068, 175,
            074, 165, 071, 134, 139, 048, 027, 166, 077, 146, 158, 231, 083, 111, 229, 122,
            060, 211, 133, 230, 220, 105, 092, 041, 055, 046, 245, 040, 244, 102, 143, 054,
            065, 025, 063, 161, 001, 216, 080, 073, 209, 076, 132, 187, 208, 089, 018, 169,
            200, 196, 135, 130, 116, 188, 159, 086, 164, 100, 109, 198, 173, 186, 003, 064,
            052, 217, 226, 250, 124, 123, 005, 202, 038, 147, 118, 126, 255, 082, 085, 212,
            207, 206, 059, 227, 047, 016, 058, 017, 182, 189, 028, 042, 223, 183, 170, 213,
            119, 248, 152, 002, 044, 154, 163, 070, 221, 153, 101, 155, 167, 043, 172, 009,
            129, 022, 039, 253, 019, 098, 108, 110, 079, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 097, 228, 251, 034, 242, 193, 238, 210, 144, 012, 191, 179, 162, 241,
            081, 051, 145, 235, 249, 014, 239, 107, 049, 192, 214, 031, 181, 199, 106, 157,
            184, 084, 204, 176, 115, 121, 050, 045, 127, 004, 150, 254, 138, 236, 205, 093,
            222, 114, 067, 029, 024, 072, 243, 141, 128, 195, 078, 066, 215, 061, 156, 180,


        };
        }
    
    public class OriginalGenerator
        {

		// Borrowing from https://flafla2.github.io/2014/08/09/perlinnoise.html which is based on https://mrl.nyu.edu/~perlin/noise/

		public OriginalGenerator(int seed, int dimensions)
        {

        }

        public void Fill(Span<byte> cube, int sides)
        {

        }

		public double Perlin(double x, double y, double z)
		{

			int xi = (int)x & 255;                              // Calculate the "unit cube" that the point asked will be located in
			int yi = (int)y & 255;                              // The left bound is ( |_x_|,|_y_|,|_z_| ) and the right bound is that
			int zi = (int)z & 255;                              // plus 1.  Next we calculate the location (from 0.0 to 1.0) in that cube.
			double xf = x - (int)x;                             // We also fade the location to smooth the result.
			double yf = y - (int)y; 
				double zf = z - (int)z;
			double u = fade(xf);
			double v = fade(yf);
			double w = fade(zf);

			int aaa, aba, aab, abb, baa, bba, bab, bbb;
			aaa = c.PO[c.PO[c.PO[xi] + yi] + zi];
			aba = c.PO[c.PO[c.PO[xi] + inc(yi)] + zi];
			aab = c.PO[c.PO[c.PO[xi] + yi] + inc(zi)];
			abb = c.PO[c.PO[c.PO[xi] + inc(yi)] + inc(zi)];
			baa = c.PO[c.PO[c.PO[inc(xi)] + yi] + zi];
			bba = c.PO[c.PO[c.PO[inc(xi)] + inc(yi)] + zi];
			bab = c.PO[c.PO[c.PO[inc(xi)] + yi] + inc(zi)];
			bbb = c.PO[c.PO[c.PO[inc(xi)] + inc(yi)] + inc(zi)];

			double x1, x2, y1, y2;
			x1 = lerp(grad(aaa, xf, yf, zf),                // The gradient function calculates the dot product between a pseudorandom
						grad(baa, xf - 1, yf, zf),              // gradient vector and the vector from the input coordinate to the 8
						u);                                     // surrounding points in its unit cube.
			x2 = lerp(grad(aba, xf, yf - 1, zf),                // This is all then lerped together as a sort of weighted average based on the faded (u,v,w)
						grad(bba, xf - 1, yf - 1, zf),              // values we made earlier.
						  u);
			y1 = lerp(x1, x2, v);

			x1 = lerp(grad(aab, xf, yf, zf - 1),
						grad(bab, xf - 1, yf, zf - 1),
						u);
			x2 = lerp(grad(abb, xf, yf - 1, zf - 1),
						  grad(bbb, xf - 1, yf - 1, zf - 1),
						  u);
			y2 = lerp(x1, x2, v);

			return (lerp(y1, y2, w) + 1) / 2;                       // For convenience we bound it to 0 - 1 (theoretical min/max before is -1 - 1)
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int inc(int num)
		{
			num++;
            return num;
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double grad(int hash, double x, double y, double z)
		{
			int h = hash & 15;                                  // Take the hashed value and take the first 4 bits of it (15 == 0b1111)
			double u = h < 8 /* 0b1000 */ ? x : y;              // If the most significant bit (MSB) of the hash is 0 then set u = x.  Otherwise y.

			double v;                                           // In Ken Perlin's original implementation this was another conditional operator (?:).  I
																// expanded it for readability.

			if (h < 4 /* 0b0100 */)                             // If the first and second significant bits are 0 set v = y
				v = y;
			else if (h == 12 /* 0b1100 */ || h == 14 /* 0b1110*/)// If the first and second significant bits are 1 set v = x
				v = x;
			else                                                // If the first and second significant bits are not equal (0/1, 1/0) set v = z
				v = z;

			return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v); // Use the last 2 bits to decide if u and v are positive or negative.  Then return their addition.
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double fade(double t)
		{
			// Fade function as defined by Ken Perlin.  This eases coordinate values
			// so that they will "ease" towards integral values.  This ends up smoothing
			// the final output.
			return t * t * t * (t * (t * 6 - 15) + 10);         // 6t^5 - 15t^4 + 10t^3
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double lerp(double a, double b, double x)
		{
            return a + x * (b - a);
		}
	}
}
