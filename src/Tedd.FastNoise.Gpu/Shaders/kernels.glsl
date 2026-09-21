// Scalar noise kernels ported from FastNoiseLite (Jordan Peck, MIT); see THIRD-PARTY-NOTICES.md.
// Arithmetic and interpolation order match the CPU kernels. Changes require GPU agreement tests.
float OpenSimplex2Kernel_Falloff2(float t, float gradient);
int NoiseMath_FastFloor(float a)
{
    // Comparison masks are all-ones (-1) when true, so adding the mask is the conditional decrement.
    int truncated = ToInt(a);
    return AddI(truncated, LessThanF(a, F(0.0)));
}
int NoiseMath_FastRound(float a)
{
    int negative = LessThanF(a, F(0.0));
    float nudged = Add(a, SelectF(negative, F(-0.5), F(0.5)));
    return ToInt(nudged);
}
float NoiseMath_Lerp(float a, float b, float t)
    { return Add(a, Mul(t, Sub(b, a))); }
float NoiseMath_InterpHermite(float t)
    { return Mul(Mul(t, t), Sub(F(3.0), Mul(F(2.0), t))); }
float NoiseMath_InterpQuintic(float t)
{
    // t * t * t * (t * (t * 6 - 15) + 10)
    float inner = Add(Mul(t, Sub(Mul(t, F(6.0)), F(15.0))), F(10.0));
    return Mul(Mul(Mul(t, t), t), inner);
}
float NoiseMath_CubicLerp(float a, float b, float c, float d, float t)
{
    float p = Sub(Sub(d, c), Sub(a, b));
    float t2 = Mul(t, t);
    float t3 = Mul(t2, t);

    // Summed strictly left to right. Float addition is not associative, so regrouping these
    // four terms shifts the result by an ULP and breaks bit-compatibility with the reference.
    float sum = Mul(t3, p);
    sum = Add(sum, Mul(t2, Sub(Sub(a, b), p)));
    sum = Add(sum, Mul(t, Sub(c, a)));
    return Add(sum, b);
}
float NoiseMath_PingPong(float t)
{
    // Truncation, not floor: matches the reference for the non-negative inputs this sees.
    float wrapped = Sub(t, Mul(ToFloat(ToInt(Mul(t, F(0.5)))), F(2.0)));
    int lower = LessThanF(wrapped, F(1.0));
    return SelectF(lower, wrapped, Sub(F(2.0), wrapped));
}
const int Hashing_PrimeX = 501125321;
const int Hashing_PrimeY = 1136930381;
const int Hashing_PrimeZ = 1720413743;
const int Hashing_Mixer = 0x27D4EB2D;
const float Hashing_IntToUnit = 1.0 / 2147483648.0;
int Hashing_Prime(int coordinate, int prime)
    { return MulI(coordinate, I(prime)); }
int Hashing_Hash2(int seed, int xPrimed, int yPrimed)
    { return MulI(XorI(XorI(seed, xPrimed), yPrimed), I(Hashing_Mixer)); }
int Hashing_Hash3(int seed, int xPrimed, int yPrimed, int zPrimed)
    { return MulI(XorI(XorI(XorI(seed, xPrimed), yPrimed), zPrimed), I(Hashing_Mixer)); }
float Hashing_ValCoord2(int seed, int xPrimed, int yPrimed)
{
    int hash = Hashing_Hash2(seed, xPrimed, yPrimed);
    hash = MulI(hash, hash);
    hash = XorI(hash, ShiftLeft(hash, 19));
    return Mul(ToFloat(hash), F(Hashing_IntToUnit));
}
float Hashing_ValCoord3(int seed, int xPrimed, int yPrimed, int zPrimed)
{
    int hash = Hashing_Hash3(seed, xPrimed, yPrimed, zPrimed);
    hash = MulI(hash, hash);
    hash = XorI(hash, ShiftLeft(hash, 19));
    return Mul(ToFloat(hash), F(Hashing_IntToUnit));
}
float Hashing_GradCoord2(int seed, int xPrimed, int yPrimed, float xd, float yd)
{
    int hash = Hashing_Hash2(seed, xPrimed, yPrimed);
    hash = XorI(hash, ShiftRightArithmetic(hash, 15));
    hash = AndI(hash, I(127 << 1));

    float xg = gradients2[hash];
    float yg = gradients2[OrI(hash, I(1))];

    return Add(Mul(xd, xg), Mul(yd, yg));
}
float Hashing_GradCoord3(int seed, int xPrimed, int yPrimed, int zPrimed, float xd, float yd, float zd)
{
    int hash = Hashing_Hash3(seed, xPrimed, yPrimed, zPrimed);
    hash = XorI(hash, ShiftRightArithmetic(hash, 15));
    hash = AndI(hash, I(63 << 2));

    float xg = gradients3[hash];
    float yg = gradients3[OrI(hash, I(1))];
    float zg = gradients3[OrI(hash, I(2))];

    return Add(Add(Mul(xd, xg), Mul(yd, yg)), Mul(zd, zg));
}

float ValueKernel_Sample2(int seed, float x, float y)
{
    int x0 = NoiseMath_FastFloor(x);
    int y0 = NoiseMath_FastFloor(y);

    float xs = NoiseMath_InterpHermite(Sub(x, ToFloat(x0)));
    float ys = NoiseMath_InterpHermite(Sub(y, ToFloat(y0)));

    x0 = Hashing_Prime(x0, Hashing_PrimeX);
    y0 = Hashing_Prime(y0, Hashing_PrimeY);
    int x1 = AddI(x0, I(Hashing_PrimeX));
    int y1 = AddI(y0, I(Hashing_PrimeY));

    float xf0 = NoiseMath_Lerp(
        Hashing_ValCoord2(seed, x0, y0),
        Hashing_ValCoord2(seed, x1, y0), xs);
    float xf1 = NoiseMath_Lerp(
        Hashing_ValCoord2(seed, x0, y1),
        Hashing_ValCoord2(seed, x1, y1), xs);

    return NoiseMath_Lerp(xf0, xf1, ys);
}
float ValueKernel_Sample3(int seed, float x, float y, float z)
{
    int x0 = NoiseMath_FastFloor(x);
    int y0 = NoiseMath_FastFloor(y);
    int z0 = NoiseMath_FastFloor(z);

    float xs = NoiseMath_InterpHermite(Sub(x, ToFloat(x0)));
    float ys = NoiseMath_InterpHermite(Sub(y, ToFloat(y0)));
    float zs = NoiseMath_InterpHermite(Sub(z, ToFloat(z0)));

    x0 = Hashing_Prime(x0, Hashing_PrimeX);
    y0 = Hashing_Prime(y0, Hashing_PrimeY);
    z0 = Hashing_Prime(z0, Hashing_PrimeZ);
    int x1 = AddI(x0, I(Hashing_PrimeX));
    int y1 = AddI(y0, I(Hashing_PrimeY));
    int z1 = AddI(z0, I(Hashing_PrimeZ));

    float xf00 = NoiseMath_Lerp(
        Hashing_ValCoord3(seed, x0, y0, z0),
        Hashing_ValCoord3(seed, x1, y0, z0), xs);
    float xf10 = NoiseMath_Lerp(
        Hashing_ValCoord3(seed, x0, y1, z0),
        Hashing_ValCoord3(seed, x1, y1, z0), xs);
    float xf01 = NoiseMath_Lerp(
        Hashing_ValCoord3(seed, x0, y0, z1),
        Hashing_ValCoord3(seed, x1, y0, z1), xs);
    float xf11 = NoiseMath_Lerp(
        Hashing_ValCoord3(seed, x0, y1, z1),
        Hashing_ValCoord3(seed, x1, y1, z1), xs);

    float yf0 = NoiseMath_Lerp(xf00, xf10, ys);
    float yf1 = NoiseMath_Lerp(xf01, xf11, ys);

    return NoiseMath_Lerp(yf0, yf1, zs);
}
const float PerlinKernel_Scale2D = 1.4247691104677813;
const float PerlinKernel_Scale3D = 0.964921414852142333984375;
float PerlinKernel_Sample2(int seed, float x, float y)
{
    int x0 = NoiseMath_FastFloor(x);
    int y0 = NoiseMath_FastFloor(y);

    float xd0 = Sub(x, ToFloat(x0));
    float yd0 = Sub(y, ToFloat(y0));
    float xd1 = Sub(xd0, F(1.0));
    float yd1 = Sub(yd0, F(1.0));

    float xs = NoiseMath_InterpQuintic(xd0);
    float ys = NoiseMath_InterpQuintic(yd0);

    x0 = Hashing_Prime(x0, Hashing_PrimeX);
    y0 = Hashing_Prime(y0, Hashing_PrimeY);
    int x1 = AddI(x0, I(Hashing_PrimeX));
    int y1 = AddI(y0, I(Hashing_PrimeY));

    float xf0 = NoiseMath_Lerp(
        Hashing_GradCoord2(seed, x0, y0, xd0, yd0),
        Hashing_GradCoord2(seed, x1, y0, xd1, yd0), xs);
    float xf1 = NoiseMath_Lerp(
        Hashing_GradCoord2(seed, x0, y1, xd0, yd1),
        Hashing_GradCoord2(seed, x1, y1, xd1, yd1), xs);

    return Mul(NoiseMath_Lerp(xf0, xf1, ys), F(PerlinKernel_Scale2D));
}
float PerlinKernel_Sample3(int seed, float x, float y, float z)
{
    int x0 = NoiseMath_FastFloor(x);
    int y0 = NoiseMath_FastFloor(y);
    int z0 = NoiseMath_FastFloor(z);

    float xd0 = Sub(x, ToFloat(x0));
    float yd0 = Sub(y, ToFloat(y0));
    float zd0 = Sub(z, ToFloat(z0));
    float xd1 = Sub(xd0, F(1.0));
    float yd1 = Sub(yd0, F(1.0));
    float zd1 = Sub(zd0, F(1.0));

    float xs = NoiseMath_InterpQuintic(xd0);
    float ys = NoiseMath_InterpQuintic(yd0);
    float zs = NoiseMath_InterpQuintic(zd0);

    x0 = Hashing_Prime(x0, Hashing_PrimeX);
    y0 = Hashing_Prime(y0, Hashing_PrimeY);
    z0 = Hashing_Prime(z0, Hashing_PrimeZ);
    int x1 = AddI(x0, I(Hashing_PrimeX));
    int y1 = AddI(y0, I(Hashing_PrimeY));
    int z1 = AddI(z0, I(Hashing_PrimeZ));

    float xf00 = NoiseMath_Lerp(
        Hashing_GradCoord3(seed, x0, y0, z0, xd0, yd0, zd0),
        Hashing_GradCoord3(seed, x1, y0, z0, xd1, yd0, zd0), xs);
    float xf10 = NoiseMath_Lerp(
        Hashing_GradCoord3(seed, x0, y1, z0, xd0, yd1, zd0),
        Hashing_GradCoord3(seed, x1, y1, z0, xd1, yd1, zd0), xs);
    float xf01 = NoiseMath_Lerp(
        Hashing_GradCoord3(seed, x0, y0, z1, xd0, yd0, zd1),
        Hashing_GradCoord3(seed, x1, y0, z1, xd1, yd0, zd1), xs);
    float xf11 = NoiseMath_Lerp(
        Hashing_GradCoord3(seed, x0, y1, z1, xd0, yd1, zd1),
        Hashing_GradCoord3(seed, x1, y1, z1, xd1, yd1, zd1), xs);

    float yf0 = NoiseMath_Lerp(xf00, xf10, ys);
    float yf1 = NoiseMath_Lerp(xf01, xf11, ys);

    return Mul(NoiseMath_Lerp(yf0, yf1, zs), F(PerlinKernel_Scale3D));
}

const float OpenSimplex2Kernel_Sqrt3 = 1.7320508075688772935274463415059;
const float OpenSimplex2Kernel_G2 = CPU_G2;
const float OpenSimplex2Kernel_C1 = CPU_C1;
const float OpenSimplex2Kernel_C2 = CPU_C2;
const float OpenSimplex2Kernel_Scale2D = 99.83685446303647;
const float OpenSimplex2Kernel_Scale3D = 32.69428253173828125;
float OpenSimplex2Kernel_Sample2(int seed, float x, float y)
{
    int i = NoiseMath_FastFloor(x);
    int j = NoiseMath_FastFloor(y);

    float xi = Sub(x, ToFloat(i));
    float yi = Sub(y, ToFloat(j));

    float t = Mul(Add(xi, yi), F(OpenSimplex2Kernel_G2));
    float x0 = Sub(xi, t);
    float y0 = Sub(yi, t);

    i = Hashing_Prime(i, Hashing_PrimeX);
    j = Hashing_Prime(j, Hashing_PrimeY);
    int px = I(Hashing_PrimeX);
    int py = I(Hashing_PrimeY);

    // Corner 0: the cell origin.
    float a = Sub(Sub(F(0.5), Mul(x0, x0)), Mul(y0, y0));
    float n0 = OpenSimplex2Kernel_Falloff2(a, Hashing_GradCoord2(seed, i, j, x0, y0));

    // Corner 2: the opposite cell corner. Its falloff is derived from `a` rather than recomputed.
    float c = Add(Mul(F(OpenSimplex2Kernel_C1), t), Add(F(OpenSimplex2Kernel_C2), a));
    float x2 = Add(x0, F(2 * OpenSimplex2Kernel_G2 - 1));
    float y2 = Add(y0, F(2 * OpenSimplex2Kernel_G2 - 1));
    float n2 = OpenSimplex2Kernel_Falloff2(
        c,
        Hashing_GradCoord2(seed, AddI(i, px), AddI(j, py), x2, y2));

    // Corner 1: whichever of the two triangles in this cell the point fell into.
    int upper = GreaterThanF(y0, x0);
    float x1 = Add(x0, SelectF(upper, F(OpenSimplex2Kernel_G2), F(OpenSimplex2Kernel_G2 - 1)));
    float y1 = Add(y0, SelectF(upper, F(OpenSimplex2Kernel_G2 - 1), F(OpenSimplex2Kernel_G2)));
    int i1 = AddI(i, SelectI(upper, I(0), px));
    int j1 = AddI(j, SelectI(upper, py, I(0)));

    float b = Sub(Sub(F(0.5), Mul(x1, x1)), Mul(y1, y1));
    float n1 = OpenSimplex2Kernel_Falloff2(b, Hashing_GradCoord2(seed, i1, j1, x1, y1));

    return Mul(Add(Add(n0, n1), n2), F(OpenSimplex2Kernel_Scale2D));
}
float OpenSimplex2Kernel_Falloff2(float t, float gradient)
{
    int inside = GreaterThanF(t, F(0.0));
    float t2 = Mul(t, t);
    return SelectF(inside, Mul(Mul(t2, t2), gradient), F(0.0));
}
float OpenSimplex2Kernel_Sample3(int seed, float x, float y, float z)
{
    int i = NoiseMath_FastRound(x);
    int j = NoiseMath_FastRound(y);
    int k = NoiseMath_FastRound(z);

    float x0 = Sub(x, ToFloat(i));
    float y0 = Sub(y, ToFloat(j));
    float z0 = Sub(z, ToFloat(k));

    // -1 when the offset is positive, +1 when negative: which neighbouring lattice point is closer.
    int one = I(1);
    int xNSign = OrI(ToInt(Sub(F(-1.0), x0)), one);
    int yNSign = OrI(ToInt(Sub(F(-1.0), y0)), one);
    int zNSign = OrI(ToInt(Sub(F(-1.0), z0)), one);

    float ax0 = Mul(ToFloat(xNSign), Neg(x0));
    float ay0 = Mul(ToFloat(yNSign), Neg(y0));
    float az0 = Mul(ToFloat(zNSign), Neg(z0));

    i = Hashing_Prime(i, Hashing_PrimeX);
    j = Hashing_Prime(j, Hashing_PrimeY);
    k = Hashing_Prime(k, Hashing_PrimeZ);

    int px = I(Hashing_PrimeX);
    int py = I(Hashing_PrimeY);
    int pz = I(Hashing_PrimeZ);
    float zero = F(0.0);

    float value = zero;
    float a = Sub(Sub(F(0.6), Mul(x0, x0)), Add(Mul(y0, y0), Mul(z0, z0)));

    // Two passes over the two offset lattices. The reference loops with an early break; unrolled
    // here so the second pass can specialise away the work that only feeds a third iteration.
    for (int pass = 0; ; pass++)
    {
        // The corner the sample sits in.
        {
            int inside = GreaterThanF(a, zero);
            float a2 = Mul(a, a);
            float contribution = Mul(Mul(a2, a2), Hashing_GradCoord3(seed, i, j, k, x0, y0, z0));
            value = Add(value, SelectF(inside, contribution, zero));
        }

        // The nearest face neighbour, along whichever axis the sample leans toward.
        {
            int leanX = AndI(NotI(LessThanF(ax0, ay0)), NotI(LessThanF(ax0, az0)));
            int leanY = AndI(
                NotI(leanX),
                AndI(GreaterThanF(ay0, ax0), NotI(LessThanF(ay0, az0))));
            int leanZ = NotI(OrI(leanX, leanY));

            float axis = SelectF(leanX, ax0, SelectF(leanY, ay0, az0));
            float b = Sub(Add(Add(a, axis), axis), F(1.0));

            int ni = SubI(i, SelectI(leanX, MulI(xNSign, px), I(0)));
            int nj = SubI(j, SelectI(leanY, MulI(yNSign, py), I(0)));
            int nk = SubI(k, SelectI(leanZ, MulI(zNSign, pz), I(0)));

            float nx = Add(x0, SelectF(leanX, ToFloat(xNSign), zero));
            float ny = Add(y0, SelectF(leanY, ToFloat(yNSign), zero));
            float nz = Add(z0, SelectF(leanZ, ToFloat(zNSign), zero));

            int inside = GreaterThanF(b, zero);
            float b2 = Mul(b, b);
            float contribution = Mul(Mul(b2, b2), Hashing_GradCoord3(seed, ni, nj, nk, nx, ny, nz));
            value = Add(value, SelectF(inside, contribution, zero));
        }

        if (pass == 1)
        {
            break;
        }

        // Step onto the second, half-offset lattice.
        ax0 = Sub(F(0.5), ax0);
        ay0 = Sub(F(0.5), ay0);
        az0 = Sub(F(0.5), az0);

        x0 = Mul(ToFloat(xNSign), ax0);
        y0 = Mul(ToFloat(yNSign), ay0);
        z0 = Mul(ToFloat(zNSign), az0);

        a = Add(a, Sub(Sub(F(0.75), ax0), Add(ay0, az0)));

        i = AddI(i, AndI(ShiftRightArithmetic(xNSign, 1), px));
        j = AddI(j, AndI(ShiftRightArithmetic(yNSign, 1), py));
        k = AddI(k, AndI(ShiftRightArithmetic(zNSign, 1), pz));

        xNSign = SubI(I(0), xNSign);
        yNSign = SubI(I(0), yNSign);
        zNSign = SubI(I(0), zNSign);

        seed = NotI(seed);
    }

    return Mul(value, F(OpenSimplex2Kernel_Scale3D));
}
