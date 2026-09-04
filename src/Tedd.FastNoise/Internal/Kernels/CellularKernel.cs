using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Internal.Kernels;

/// <summary>
/// Worley cellular noise: scatter one feature point per lattice cell, then report something about
/// the distance to the nearest ones.
/// </summary>
/// <remarks>
/// <para>
/// Ported from FastNoiseLite 1.1.1 by Jordan Peck (MIT); see THIRD-PARTY-NOTICES.md.
/// </para>
/// <para>
/// Cost scales badly with dimension -- 9 cells in 2D, 27 in 3D, each with a table gather -- which
/// makes this the second most expensive kernel after cubic value noise. It also vectorises
/// perfectly: the cell loops have fixed trip counts, and the "is this the new nearest" test is a
/// select rather than a branch, so all lanes stay in lockstep.
/// </para>
/// <para>
/// The distance metric is a branch on a method argument rather than a generic parameter. It is
/// hoisted out of nothing and predicted perfectly, and it keeps this to one loop body instead of
/// three near-copies.
/// </para>
/// </remarks>
internal static class CellularKernel
{
    /// <summary>Feature-point displacement in 2D, tuned so cells stay convex at full jitter.</summary>
    private const float Jitter2D = 0.43701595f;

    /// <summary>Feature-point displacement in 3D, tuned so cells stay convex at full jitter.</summary>
    private const float Jitter3D = 0.39614353f;

    /// <summary>Scales the winning cell hash to [-1, 1) for <see cref="CellularReturnType.CellValue"/>.</summary>
    private const float IntToUnit = 1f / 2147483648f;

    /// <summary>Samples 2D cellular noise.</summary>
    public static TF Sample2<TOps, TF, TI>(
        TI seed,
        TF x,
        TF y,
        CellularDistanceFunction distanceFunction,
        CellularReturnType returnType,
        float jitterModifier)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI xr = NoiseMath.FastRound<TOps, TF, TI>(x);
        TI yr = NoiseMath.FastRound<TOps, TF, TI>(y);

        TF distance0 = TOps.F(float.MaxValue);
        TF distance1 = TOps.F(float.MaxValue);
        TI closestHash = TOps.I(0);

        TF jitter = TOps.F(Jitter2D * jitterModifier);

        TI one = TOps.I(1);
        TI px = TOps.I(Hashing.PrimeX);
        TI py = TOps.I(Hashing.PrimeY);

        TI xPrimed = Hashing.Prime<TOps, TF, TI>(TOps.SubI(xr, one), Hashing.PrimeX);
        TI yPrimedBase = Hashing.Prime<TOps, TF, TI>(TOps.SubI(yr, one), Hashing.PrimeY);

        TF xiBase = TOps.ToFloat(TOps.SubI(xr, one));
        TF yiBase = TOps.ToFloat(TOps.SubI(yr, one));

        for (int xOffset = 0; xOffset < 3; xOffset++)
        {
            TF vecXBase = TOps.Sub(TOps.Add(xiBase, TOps.F(xOffset)), x);
            TI yPrimed = yPrimedBase;

            for (int yOffset = 0; yOffset < 3; yOffset++)
            {
                TI hash = Hashing.Hash2<TOps, TF, TI>(seed, xPrimed, yPrimed);
                TI index = TOps.AndI(hash, TOps.I(255 << 1));

                TF vecX = TOps.Add(vecXBase, TOps.Mul(TOps.Gather(Tables.RandVecs2D, index), jitter));
                TF vecY = TOps.Add(
                    TOps.Sub(TOps.Add(yiBase, TOps.F(yOffset)), y),
                    TOps.Mul(TOps.Gather(Tables.RandVecs2D, TOps.OrI(index, one)), jitter));

                TF newDistance;
                if (distanceFunction == CellularDistanceFunction.Manhattan)
                {
                    newDistance = TOps.Add(TOps.Abs(vecX), TOps.Abs(vecY));
                }
                else if (distanceFunction == CellularDistanceFunction.Hybrid)
                {
                    newDistance = TOps.Add(
                        TOps.Add(TOps.Abs(vecX), TOps.Abs(vecY)),
                        TOps.Add(TOps.Mul(vecX, vecX), TOps.Mul(vecY, vecY)));
                }
                else
                {
                    newDistance = TOps.Add(TOps.Mul(vecX, vecX), TOps.Mul(vecY, vecY));
                }

                // Second-nearest first: it is defined against the *previous* nearest.
                distance1 = TOps.Max(TOps.Min(distance1, newDistance), distance0);

                TI closer = TOps.LessThanF(newDistance, distance0);
                distance0 = TOps.SelectF(closer, newDistance, distance0);
                closestHash = TOps.SelectI(closer, hash, closestHash);

                yPrimed = TOps.AddI(yPrimed, py);
            }

            xPrimed = TOps.AddI(xPrimed, px);
        }

        return Finish<TOps, TF, TI>(distance0, distance1, closestHash, distanceFunction, returnType);
    }

    /// <summary>Samples 3D cellular noise.</summary>
    public static TF Sample3<TOps, TF, TI>(
        TI seed,
        TF x,
        TF y,
        TF z,
        CellularDistanceFunction distanceFunction,
        CellularReturnType returnType,
        float jitterModifier)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        TI xr = NoiseMath.FastRound<TOps, TF, TI>(x);
        TI yr = NoiseMath.FastRound<TOps, TF, TI>(y);
        TI zr = NoiseMath.FastRound<TOps, TF, TI>(z);

        TF distance0 = TOps.F(float.MaxValue);
        TF distance1 = TOps.F(float.MaxValue);
        TI closestHash = TOps.I(0);

        TF jitter = TOps.F(Jitter3D * jitterModifier);

        TI one = TOps.I(1);
        TI two = TOps.I(2);
        TI px = TOps.I(Hashing.PrimeX);
        TI py = TOps.I(Hashing.PrimeY);
        TI pz = TOps.I(Hashing.PrimeZ);

        TI xPrimed = Hashing.Prime<TOps, TF, TI>(TOps.SubI(xr, one), Hashing.PrimeX);
        TI yPrimedBase = Hashing.Prime<TOps, TF, TI>(TOps.SubI(yr, one), Hashing.PrimeY);
        TI zPrimedBase = Hashing.Prime<TOps, TF, TI>(TOps.SubI(zr, one), Hashing.PrimeZ);

        TF xiBase = TOps.ToFloat(TOps.SubI(xr, one));
        TF yiBase = TOps.ToFloat(TOps.SubI(yr, one));
        TF ziBase = TOps.ToFloat(TOps.SubI(zr, one));

        for (int xOffset = 0; xOffset < 3; xOffset++)
        {
            TF vecXBase = TOps.Sub(TOps.Add(xiBase, TOps.F(xOffset)), x);
            TI yPrimed = yPrimedBase;

            for (int yOffset = 0; yOffset < 3; yOffset++)
            {
                TF vecYBase = TOps.Sub(TOps.Add(yiBase, TOps.F(yOffset)), y);
                TI zPrimed = zPrimedBase;

                for (int zOffset = 0; zOffset < 3; zOffset++)
                {
                    TI hash = Hashing.Hash3<TOps, TF, TI>(seed, xPrimed, yPrimed, zPrimed);
                    TI index = TOps.AndI(hash, TOps.I(255 << 2));

                    TF vecX = TOps.Add(vecXBase, TOps.Mul(TOps.Gather(Tables.RandVecs3D, index), jitter));
                    TF vecY = TOps.Add(vecYBase, TOps.Mul(TOps.Gather(Tables.RandVecs3D, TOps.OrI(index, one)), jitter));
                    TF vecZ = TOps.Add(
                        TOps.Sub(TOps.Add(ziBase, TOps.F(zOffset)), z),
                        TOps.Mul(TOps.Gather(Tables.RandVecs3D, TOps.OrI(index, two)), jitter));

                    TF newDistance;
                    if (distanceFunction == CellularDistanceFunction.Manhattan)
                    {
                        newDistance = TOps.Add(TOps.Add(TOps.Abs(vecX), TOps.Abs(vecY)), TOps.Abs(vecZ));
                    }
                    else if (distanceFunction == CellularDistanceFunction.Hybrid)
                    {
                        newDistance = TOps.Add(
                            TOps.Add(TOps.Add(TOps.Abs(vecX), TOps.Abs(vecY)), TOps.Abs(vecZ)),
                            TOps.Add(TOps.Add(TOps.Mul(vecX, vecX), TOps.Mul(vecY, vecY)), TOps.Mul(vecZ, vecZ)));
                    }
                    else
                    {
                        newDistance = TOps.Add(
                            TOps.Add(TOps.Mul(vecX, vecX), TOps.Mul(vecY, vecY)),
                            TOps.Mul(vecZ, vecZ));
                    }

                    distance1 = TOps.Max(TOps.Min(distance1, newDistance), distance0);

                    TI closer = TOps.LessThanF(newDistance, distance0);
                    distance0 = TOps.SelectF(closer, newDistance, distance0);
                    closestHash = TOps.SelectI(closer, hash, closestHash);

                    zPrimed = TOps.AddI(zPrimed, pz);
                }

                yPrimed = TOps.AddI(yPrimed, py);
            }

            xPrimed = TOps.AddI(xPrimed, px);
        }

        return Finish<TOps, TF, TI>(distance0, distance1, closestHash, distanceFunction, returnType);
    }

    /// <summary>Turns the two nearest distances and the winning hash into the requested output.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TF Finish<TOps, TF, TI>(
        TF distance0,
        TF distance1,
        TI closestHash,
        CellularDistanceFunction distanceFunction,
        CellularReturnType returnType)
        where TOps : ISimdOps<TF, TI>
        where TF : struct
        where TI : struct
    {
        // Squared distances are what the loop accumulates; only true Euclidean pays for the root.
        if (distanceFunction == CellularDistanceFunction.Euclidean && returnType >= CellularReturnType.Distance)
        {
            distance0 = TOps.Sqrt(distance0);

            if (returnType >= CellularReturnType.Distance2)
            {
                distance1 = TOps.Sqrt(distance1);
            }
        }

        TF one = TOps.F(1f);

        return returnType switch
        {
            CellularReturnType.CellValue => TOps.Mul(TOps.ToFloat(closestHash), TOps.F(IntToUnit)),
            CellularReturnType.Distance => TOps.Sub(distance0, one),
            CellularReturnType.Distance2 => TOps.Sub(distance1, one),
            CellularReturnType.Distance2Add => TOps.Sub(TOps.Mul(TOps.Add(distance1, distance0), TOps.F(0.5f)), one),
            CellularReturnType.Distance2Sub => TOps.Sub(TOps.Sub(distance1, distance0), one),
            CellularReturnType.Distance2Mul => TOps.Sub(TOps.Mul(TOps.Mul(distance1, distance0), TOps.F(0.5f)), one),
            CellularReturnType.Distance2Div => TOps.Sub(TOps.Div(distance0, distance1), one),
            _ => TOps.F(0f),
        };
    }
}
