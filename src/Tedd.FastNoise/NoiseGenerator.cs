using System;
using Tedd.FastNoise.Internal;
using Tedd.FastNoise.Internal.Kernels;

namespace Tedd.FastNoise;

/// <summary>
/// A configured noise function: one algorithm, optionally layered into a fractal, sampled as
/// single points or filled in bulk across a grid or volume.
/// </summary>
/// <remarks>
/// <para>
/// The settings mirror FastNoiseLite, and for any given configuration this produces the same
/// values FastNoiseLite does, bit for bit. What is added is bulk generation: SIMD and multi-core
/// fills over a <see cref="GridRegion2D"/> or <see cref="GridRegion3D"/>, which is where the
/// speedups live. A single <see cref="GetNoise(float, float)"/> call has nothing to vectorise.
/// </para>
/// <para>
/// <b>Threading.</b> Configure, then sample. Property setters are not safe against concurrent
/// sampling, but once a generator is configured any number of threads may call
/// <see cref="GetNoise(float, float)"/> and the fill methods on it simultaneously.
/// </para>
/// <example>
/// A heightmap for one 16x16 chunk:
/// <code>
/// var noise = new NoiseGenerator(seed: 1337)
/// {
///     NoiseType = NoiseType.OpenSimplex2,
///     FractalType = FractalType.FBm,
///     Octaves = 5,
///     Frequency = 0.005f,
/// };
///
/// var heights = new float[16 * 16];
/// noise.Fill(heights, new GridRegion2D(chunkX * 16, chunkZ * 16, 16, 16));
/// </code>
/// </example>
/// </remarks>
public sealed partial class NoiseGenerator
{
    private int _seed;
    private float _frequency = 0.01f;
    private NoiseType _noiseType = NoiseType.OpenSimplex2;
    private RotationType3D _rotationType3D = RotationType3D.None;

    private FractalType _fractalType = FractalType.None;
    private int _octaves = 3;
    private float _lacunarity = 2f;
    private float _gain = 0.5f;
    private float _weightedStrength;
    private float _pingPongStrength = 2f;
    private float _fractalBounding = 1f / 1.75f;

    private CellularDistanceFunction _cellularDistanceFunction = CellularDistanceFunction.EuclideanSq;
    private CellularReturnType _cellularReturnType = CellularReturnType.Distance;
    private float _cellularJitter = 1f;

    private DomainWarpType _domainWarpType = DomainWarpType.OpenSimplex2;
    private float _domainWarpAmplitude = 1f;

    private TransformType3D _transformType3D = TransformType3D.DefaultOpenSimplex2;

    /// <summary>
    /// The vendored reference, kept in step with the properties above.
    /// </summary>
    /// <remarks>
    /// Used for the two things the wide kernels do not cover: OpenSimplex2S, and every domain warp.
    /// Rebuilt on demand rather than on every setter so that configuring a generator stays cheap.
    /// </remarks>
    private FastNoiseLiteCore? _core;

    /// <summary>Creates a generator with the given seed and FastNoiseLite's defaults for everything else.</summary>
    /// <param name="seed">Any 32-bit value. Two generators with different seeds produce uncorrelated fields.</param>
    public NoiseGenerator(int seed = 1337)
    {
        _seed = seed;
        CalculateFractalBounding();
        UpdateTransformType3D();
    }

    /// <summary>Selects which world this generator produces. Uncorrelated between values.</summary>
    public int Seed
    {
        get => _seed;
        set
        {
            _seed = value;
            _core = null;
        }
    }

    /// <summary>
    /// Cycles per world unit. Larger values pack features closer together.
    /// </summary>
    /// <remarks>
    /// The reciprocal is the more useful intuition: at 0.01 the base octave has a wavelength of
    /// 100 world units, so hills are roughly 100 blocks apart.
    /// </remarks>
    public float Frequency
    {
        get => _frequency;
        set
        {
            _frequency = value;
            _core = null;
        }
    }

    /// <summary>Which algorithm produces each octave.</summary>
    public NoiseType NoiseType
    {
        get => _noiseType;
        set
        {
            _noiseType = value;
            UpdateTransformType3D();
            _core = null;
        }
    }

    /// <summary>Optional rotation of the 3D domain. See <see cref="RotationType3D"/> for when it matters.</summary>
    public RotationType3D RotationType3D
    {
        get => _rotationType3D;
        set
        {
            _rotationType3D = value;
            UpdateTransformType3D();
            _core = null;
        }
    }

    /// <summary>How octaves combine, or <see cref="FractalType.None"/> for a single octave.</summary>
    public FractalType FractalType
    {
        get => _fractalType;
        set
        {
            _fractalType = value;
            _core = null;
        }
    }

    /// <summary>
    /// Number of octaves summed. Cost is linear in this; detail is not, because each octave
    /// contributes <see cref="Gain"/> times less than the one before.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int Octaves
    {
        get => _octaves;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _octaves = value;
            CalculateFractalBounding();
            _core = null;
        }
    }

    /// <summary>Frequency multiplier between octaves. 2.0 is the conventional choice.</summary>
    public float Lacunarity
    {
        get => _lacunarity;
        set
        {
            _lacunarity = value;
            _core = null;
        }
    }

    /// <summary>Amplitude multiplier between octaves. 0.5 is the conventional choice.</summary>
    public float Gain
    {
        get => _gain;
        set
        {
            _gain = value;
            CalculateFractalBounding();
            _core = null;
        }
    }

    /// <summary>
    /// Biases each octave's amplitude by the previous octave's value, in [0, 1].
    /// </summary>
    /// <remarks>
    /// At zero, every octave contributes evenly everywhere. Turned up, high ground gets rougher and
    /// low ground gets smoother, which reads as erosion: exposed peaks are jagged, valley floors
    /// are not. It also makes octaves sequentially dependent, which prevents a stack from being
    /// flattened by <see cref="NoiseStack.Compile"/>.
    /// </remarks>
    public float WeightedStrength
    {
        get => _weightedStrength;
        set
        {
            _weightedStrength = value;
            _core = null;
        }
    }

    /// <summary>Number of folds per octave for <see cref="FractalType.PingPong"/>.</summary>
    public float PingPongStrength
    {
        get => _pingPongStrength;
        set
        {
            _pingPongStrength = value;
            _core = null;
        }
    }

    /// <summary>Distance metric for <see cref="NoiseType.Cellular"/>.</summary>
    public CellularDistanceFunction CellularDistanceFunction
    {
        get => _cellularDistanceFunction;
        set
        {
            _cellularDistanceFunction = value;
            _core = null;
        }
    }

    /// <summary>Output selection for <see cref="NoiseType.Cellular"/>.</summary>
    public CellularReturnType CellularReturnType
    {
        get => _cellularReturnType;
        set
        {
            _cellularReturnType = value;
            _core = null;
        }
    }

    /// <summary>
    /// How far cellular feature points wander from their cell centres, in [0, 1].
    /// </summary>
    /// <remarks>
    /// Zero puts every point at its cell centre, giving a perfect grid. One is the largest
    /// displacement that keeps cells well formed; beyond that they start to swallow each other and
    /// the distance field develops creases.
    /// </remarks>
    public float CellularJitter
    {
        get => _cellularJitter;
        set
        {
            _cellularJitter = value;
            _core = null;
        }
    }

    /// <summary>Which noise displaces coordinates in <see cref="DomainWarp(ref float, ref float)"/>.</summary>
    public DomainWarpType DomainWarpType
    {
        get => _domainWarpType;
        set
        {
            _domainWarpType = value;
            _core = null;
        }
    }

    /// <summary>How far <see cref="DomainWarp(ref float, ref float)"/> may displace a coordinate, in world units.</summary>
    public float DomainWarpAmplitude
    {
        get => _domainWarpAmplitude;
        set
        {
            _domainWarpAmplitude = value;
            _core = null;
        }
    }

    /// <summary>
    /// How much detail to compute relative to the sample spacing of a fill. Off by default.
    /// </summary>
    /// <remarks>
    /// Only consulted by the fill methods, which know their sample spacing;
    /// <see cref="GetNoise(float, float)"/> has no spacing to reason about and always runs every
    /// octave. See <see cref="LodPolicy"/> for why this exists.
    /// </remarks>
    public LodPolicy Lod { get; set; } = LodPolicy.Disabled;

    /// <summary>Samples the noise at one 2D point. Output is approximately [-1, 1].</summary>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    public float GetNoise(float x, float y)
    {
        if (_noiseType == NoiseType.OpenSimplex2S)
        {
            return Core().GetNoise(x, y);
        }

        NoisePipeline.Transform2<ScalarOps, float, int>(_noiseType, _frequency, ref x, ref y);
        KernelConfig kernel = BuildKernelConfig();
        FractalConfig fractal = BuildFractalConfig();

        return FractalKernel.Fractal2<ScalarOps, float, int>(kernel, fractal, _seed, x, y, _octaves, 1f);
    }

    /// <summary>Samples the noise at one 3D point. Output is approximately [-1, 1].</summary>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    /// <param name="z">World Z.</param>
    public float GetNoise(float x, float y, float z)
    {
        if (_noiseType == NoiseType.OpenSimplex2S)
        {
            return Core().GetNoise(x, y, z);
        }

        NoisePipeline.Transform3<ScalarOps, float, int>(_transformType3D, _frequency, ref x, ref y, ref z);
        KernelConfig kernel = BuildKernelConfig();
        FractalConfig fractal = BuildFractalConfig();

        return FractalKernel.Fractal3<ScalarOps, float, int>(kernel, fractal, _seed, x, y, z, _octaves, 1f);
    }

    /// <summary>
    /// Displaces a 2D coordinate by an independent noise field.
    /// </summary>
    /// <param name="x">World X, replaced by the warped value.</param>
    /// <param name="y">World Y, replaced by the warped value.</param>
    /// <remarks>
    /// Warp the coordinate, then sample noise at the warped position, and features stop looking
    /// like they were generated on a grid: coastlines meander, strata fold, ridges wander.
    /// Set <see cref="FractalType"/> to one of the <c>DomainWarp</c> values to layer the warp itself.
    /// </remarks>
    public void DomainWarp(ref float x, ref float y) => Core().DomainWarp(ref x, ref y);

    /// <summary>Displaces a 3D coordinate by an independent noise field.</summary>
    /// <param name="x">World X, replaced by the warped value.</param>
    /// <param name="y">World Y, replaced by the warped value.</param>
    /// <param name="z">World Z, replaced by the warped value.</param>
    public void DomainWarp(ref float x, ref float y, ref float z) => Core().DomainWarp(ref x, ref y, ref z);

    /// <summary>Recomputes the amplitude normaliser after a change to octave count or gain.</summary>
    private void CalculateFractalBounding()
    {
        float gain = MathF.Abs(_gain);
        float amp = gain;
        float total = 1f;

        for (int octave = 1; octave < _octaves; octave++)
        {
            total += amp;
            amp *= gain;
        }

        _fractalBounding = 1f / total;
    }

    /// <summary>Resolves the 3D domain rotation from the noise type and the rotation setting.</summary>
    private void UpdateTransformType3D()
        => _transformType3D = _rotationType3D switch
        {
            RotationType3D.ImproveXYPlanes => TransformType3D.ImproveXYPlanes,
            RotationType3D.ImproveXZPlanes => TransformType3D.ImproveXZPlanes,
            _ => _noiseType is NoiseType.OpenSimplex2 or NoiseType.OpenSimplex2S
                ? TransformType3D.DefaultOpenSimplex2
                : TransformType3D.None,
        };

    /// <summary>Snapshots the algorithm settings for a fill.</summary>
    internal KernelConfig BuildKernelConfig() => new()
    {
        NoiseType = _noiseType,
        Transform3D = _transformType3D,
        CellularDistance = _cellularDistanceFunction,
        CellularReturn = _cellularReturnType,
        CellularJitter = _cellularJitter,
    };

    /// <summary>Snapshots the octave settings for a fill.</summary>
    internal FractalConfig BuildFractalConfig() => new()
    {
        Type = _fractalType,
        Octaves = _octaves,
        Lacunarity = _lacunarity,
        Gain = _gain,
        WeightedStrength = _weightedStrength,
        PingPongStrength = _pingPongStrength,
        Bounding = _fractalBounding,
    };

    /// <summary>True when octaves can be flattened into a single list by <see cref="NoiseStack.Compile"/>.</summary>
    internal bool OctavesAreIndependent => _weightedStrength == 0f;

    /// <summary>Builds, or returns, the reference instance mirroring this generator's settings.</summary>
    private FastNoiseLiteCore Core()
    {
        FastNoiseLiteCore? core = _core;
        if (core is not null)
        {
            return core;
        }

        core = new FastNoiseLiteCore(_seed);
        core.SetFrequency(_frequency);
        core.SetNoiseType((FastNoiseLiteCore.NoiseType)_noiseType);
        core.SetRotationType3D((FastNoiseLiteCore.RotationType3D)_rotationType3D);
        core.SetFractalType((FastNoiseLiteCore.FractalType)_fractalType);
        core.SetFractalOctaves(_octaves);
        core.SetFractalLacunarity(_lacunarity);
        core.SetFractalGain(_gain);
        core.SetFractalWeightedStrength(_weightedStrength);
        core.SetFractalPingPongStrength(_pingPongStrength);
        core.SetCellularDistanceFunction((FastNoiseLiteCore.CellularDistanceFunction)_cellularDistanceFunction);
        core.SetCellularReturnType((FastNoiseLiteCore.CellularReturnType)_cellularReturnType);
        core.SetCellularJitter(_cellularJitter);
        core.SetDomainWarpType((FastNoiseLiteCore.DomainWarpType)_domainWarpType);
        core.SetDomainWarpAmp(_domainWarpAmplitude);

        // Benign race: two threads may each build one and one wins. Both are equivalent.
        _core = core;
        return core;
    }

    /// <summary>Exposes the reference instance to the fill paths that need it.</summary>
    internal FastNoiseLiteCore ReferenceCore() => Core();
}
