namespace Tedd.FastNoise;

/// <summary>
/// The coherent-noise algorithm used to produce a single octave.
/// </summary>
/// <remarks>
/// Values match FastNoiseLite's <c>NoiseType</c> ordering so configurations port across directly.
/// </remarks>
public enum NoiseType
{
    /// <summary>
    /// Simplex-lattice gradient noise. No preferred directions, so no axis-aligned creasing.
    /// The best general default, and the right choice for 3D density fields.
    /// </summary>
    OpenSimplex2 = 0,

    /// <summary>
    /// The smoother OpenSimplex2 variant, with a larger kernel radius and more corners per sample.
    /// Softer output at roughly twice the cost. Scalar only -- see <see cref="NoiseBackend"/>.
    /// </summary>
    OpenSimplex2S = 1,

    /// <summary>
    /// Worley cellular noise. Produces cell structure rather than smooth undulation: caves, ore
    /// pockets, biome regions, cracked surfaces. Configure with <see cref="CellularDistanceFunction"/>
    /// and <see cref="CellularReturnType"/>.
    /// </summary>
    Cellular = 2,

    /// <summary>
    /// Classic Perlin gradient noise on a cubic lattice. Cheap and familiar, with mild axis
    /// alignment that is usually invisible in a heightmap and visible in a 3D density field.
    /// </summary>
    Perlin = 3,

    /// <summary>
    /// Value noise with cubic interpolation. Smooth, but reads 4^n lattice points per sample --
    /// 64 in 3D. Use sparingly and at low frequency.
    /// </summary>
    ValueCubic = 4,

    /// <summary>
    /// Value noise with cubic-Hermite interpolation. The cheapest kernel and the only one that
    /// needs no table lookups, at the cost of visible lattice structure.
    /// </summary>
    Value = 5,
}

/// <summary>Optional rotation applied to 3D coordinates before sampling.</summary>
/// <remarks>
/// Gradient noise sampled on a plane through a 3D field shows more structure along the lattice
/// axes than a true 2D slice would. If you generate a heightmap by sampling a 3D field at fixed
/// height, or animate 2D noise by sweeping one axis, rotating the domain hides that.
/// </remarks>
public enum RotationType3D
{
    /// <summary>No rotation beyond whatever the noise type applies itself.</summary>
    None = 0,

    /// <summary>Orient the lattice so XY planes slice cleanly. Use when Z is time or depth.</summary>
    ImproveXYPlanes = 1,

    /// <summary>Orient the lattice so XZ planes slice cleanly. Use when Y is up, as in most voxel worlds.</summary>
    ImproveXZPlanes = 2,
}

/// <summary>How successive octaves are combined into a single value.</summary>
public enum FractalType
{
    /// <summary>No layering: a single octave of the base noise.</summary>
    None = 0,

    /// <summary>
    /// Fractional Brownian motion: the standard sum of octaves at rising frequency and falling
    /// amplitude. Rolling, natural-looking terrain.
    /// </summary>
    FBm = 1,

    /// <summary>
    /// Mirrors each octave around zero and inverts it, so octave minima become sharp creases.
    /// Mountain ridges, canyon networks, eroded rock.
    /// </summary>
    Ridged = 2,

    /// <summary>
    /// Folds each octave back and forth through a range before summing. Produces banded, terraced
    /// structure -- sedimentary strata, stepped plateaus.
    /// </summary>
    PingPong = 3,

    /// <summary>Domain warp only: each warp octave is applied to coordinates already warped by the previous one.</summary>
    DomainWarpProgressive = 4,

    /// <summary>Domain warp only: every warp octave reads the original coordinates.</summary>
    DomainWarpIndependent = 5,
}

/// <summary>Distance metric used by <see cref="NoiseType.Cellular"/>.</summary>
public enum CellularDistanceFunction
{
    /// <summary>Straight-line distance. Round cells.</summary>
    Euclidean = 0,

    /// <summary>Squared straight-line distance. The same cell shapes without the square root, biased toward zero.</summary>
    EuclideanSq = 1,

    /// <summary>Sum of absolute component differences. Diamond cells with axis-aligned edges.</summary>
    Manhattan = 2,

    /// <summary>Manhattan plus squared Euclidean. Cells with softened corners.</summary>
    Hybrid = 3,
}

/// <summary>What a <see cref="NoiseType.Cellular"/> sample returns.</summary>
public enum CellularReturnType
{
    /// <summary>A flat pseudo-random value per cell. Use for region or biome indices.</summary>
    CellValue = 0,

    /// <summary>Distance to the nearest feature point.</summary>
    Distance = 1,

    /// <summary>Distance to the second-nearest feature point.</summary>
    Distance2 = 2,

    /// <summary>Mean of the two nearest distances.</summary>
    Distance2Add = 3,

    /// <summary>Second-nearest minus nearest. Near zero on cell boundaries: the classic crack pattern.</summary>
    Distance2Sub = 4,

    /// <summary>Half the product of the two nearest distances.</summary>
    Distance2Mul = 5,

    /// <summary>Nearest divided by second-nearest.</summary>
    Distance2Div = 6,
}

/// <summary>The noise used to offset coordinates during domain warping.</summary>
public enum DomainWarpType
{
    /// <summary>Simplex gradient warping. Smooth and isotropic.</summary>
    OpenSimplex2 = 0,

    /// <summary>A cheaper simplex warp that drops one corner.</summary>
    OpenSimplex2Reduced = 1,

    /// <summary>Lattice-interpolated warping. The cheapest option, with mild grid structure.</summary>
    BasicGrid = 2,
}

/// <summary>
/// Execution strategy for bulk fills.
/// </summary>
/// <remarks>
/// <para>
/// Single-point sampling always runs scalar; this only affects the <c>Fill</c> family.
/// </para>
/// <para>
/// Every backend produces bit-identical results. That is a hard guarantee, not an aspiration:
/// the kernels are one generic body instantiated per lane width, fused multiply-add is deliberately
/// avoided, and the test suite compares backends for exact equality. A client on ARM and a server
/// on AVX-512 will agree on the terrain.
/// </para>
/// </remarks>
public enum NoiseBackend
{
    /// <summary>
    /// Choose per call: parallel SIMD for large fills, single-threaded SIMD for medium ones,
    /// scalar for fills too small to amortise the setup. The right answer almost always.
    /// </summary>
    Auto = 0,

    /// <summary>Force the portable scalar path.</summary>
    Scalar = 1,

    /// <summary>Force single-threaded SIMD. Degrades to scalar where the runtime reports no vector acceleration, and for noise types with no wide kernel.</summary>
    Simd = 2,

    /// <summary>SIMD across all cores. Degrades to <see cref="Simd"/> for fills too small to partition.</summary>
    Parallel = 3,

    /// <summary>
    /// GPU compute, provided by the optional <c>Tedd.FastNoise.Gpu</c> package. Degrades to
    /// <see cref="Parallel"/> when no accelerator is registered or the fill is too small to pay
    /// for the round trip.
    /// </summary>
    Gpu = 4,
}
