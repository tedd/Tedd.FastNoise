using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Designer.ViewModels;

/// <summary>
/// One editable layer: everything a <see cref="NoiseGenerator"/> exposes, plus how the layer sits
/// in the stack.
/// </summary>
/// <remarks>
/// Holds plain values rather than a live <see cref="NoiseGenerator"/> so the UI can be edited
/// freely and a generator built only when a preview is actually requested.
/// </remarks>
public sealed class LayerViewModel : ObservableObject
{
    private string _name = "layer";
    private bool _isEnabled = true;

    private int _seed = 1337;
    private float _frequency = 0.01f;
    private NoiseType _noiseType = NoiseType.OpenSimplex2;
    private RotationType3D _rotation = RotationType3D.None;

    private FractalType _fractalType = FractalType.FBm;
    private int _octaves = 4;
    private float _lacunarity = 2f;
    private float _gain = 0.5f;
    private float _weightedStrength;
    private float _pingPongStrength = 2f;

    private CellularDistanceFunction _cellularDistance = CellularDistanceFunction.EuclideanSq;
    private CellularReturnType _cellularReturn = CellularReturnType.Distance;
    private float _cellularJitter = 1f;

    private LayerBlend _blend = LayerBlend.Add;
    private float _amplitude = 1f;
    private float _offset;
    private float _blendFactor = 0.5f;
    private float _featureSize;

    /// <summary>Raised whenever any property changes, so the shell can queue a re-render.</summary>
    public event EventHandler? Changed;

    /// <summary>Display name. Also used as the variable name in generated code.</summary>
    public string Name
    {
        get => _name;
        set => SetAndNotify(ref _name, value);
    }

    /// <summary>Whether this layer takes part in the preview and in generated code.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndNotify(ref _isEnabled, value);
    }

    /// <summary>Seed for this layer's noise.</summary>
    public int Seed
    {
        get => _seed;
        set => SetAndNotify(ref _seed, value);
    }

    /// <summary>Cycles per world unit.</summary>
    public float Frequency
    {
        get => _frequency;
        set => SetAndNotify(ref _frequency, value);
    }

    /// <summary>Algorithm.</summary>
    public NoiseType NoiseType
    {
        get => _noiseType;
        set
        {
            if (SetAndNotify(ref _noiseType, value))
            {
                Raise(nameof(IsCellular));
            }
        }
    }

    /// <summary>3D domain rotation.</summary>
    public RotationType3D Rotation
    {
        get => _rotation;
        set => SetAndNotify(ref _rotation, value);
    }

    /// <summary>How octaves combine.</summary>
    public FractalType FractalType
    {
        get => _fractalType;
        set
        {
            if (SetAndNotify(ref _fractalType, value))
            {
                Raise(nameof(IsFractal));
                Raise(nameof(IsPingPong));
            }
        }
    }

    /// <summary>Octave count.</summary>
    public int Octaves
    {
        get => _octaves;
        set => SetAndNotify(ref _octaves, Math.Max(1, value));
    }

    /// <summary>Frequency multiplier between octaves.</summary>
    public float Lacunarity
    {
        get => _lacunarity;
        set => SetAndNotify(ref _lacunarity, value);
    }

    /// <summary>Amplitude multiplier between octaves.</summary>
    public float Gain
    {
        get => _gain;
        set => SetAndNotify(ref _gain, value);
    }

    /// <summary>Per-octave amplitude bias from the previous octave.</summary>
    public float WeightedStrength
    {
        get => _weightedStrength;
        set => SetAndNotify(ref _weightedStrength, value);
    }

    /// <summary>Folds per octave for ping-pong fractals.</summary>
    public float PingPongStrength
    {
        get => _pingPongStrength;
        set => SetAndNotify(ref _pingPongStrength, value);
    }

    /// <summary>Cellular distance metric.</summary>
    public CellularDistanceFunction CellularDistance
    {
        get => _cellularDistance;
        set => SetAndNotify(ref _cellularDistance, value);
    }

    /// <summary>Cellular output selection.</summary>
    public CellularReturnType CellularReturn
    {
        get => _cellularReturn;
        set => SetAndNotify(ref _cellularReturn, value);
    }

    /// <summary>Cellular feature-point displacement.</summary>
    public float CellularJitter
    {
        get => _cellularJitter;
        set => SetAndNotify(ref _cellularJitter, value);
    }

    /// <summary>How this layer folds into the ones below it.</summary>
    public LayerBlend Blend
    {
        get => _blend;
        set
        {
            if (SetAndNotify(ref _blend, value))
            {
                Raise(nameof(IsLerp));
            }
        }
    }

    /// <summary>Multiplier applied before blending.</summary>
    public float Amplitude
    {
        get => _amplitude;
        set => SetAndNotify(ref _amplitude, value);
    }

    /// <summary>Constant added before blending.</summary>
    public float Offset
    {
        get => _offset;
        set => SetAndNotify(ref _offset, value);
    }

    /// <summary>Interpolation weight for the Lerp blend.</summary>
    public float BlendFactor
    {
        get => _blendFactor;
        set => SetAndNotify(ref _blendFactor, value);
    }

    /// <summary>Smallest feature in world units, for level-of-detail culling. Zero never culls.</summary>
    public float FeatureSize
    {
        get => _featureSize;
        set => SetAndNotify(ref _featureSize, value);
    }

    /// <summary>Whether the cellular settings apply to the current noise type.</summary>
    public bool IsCellular => _noiseType == NoiseType.Cellular;

    /// <summary>Whether the octave settings apply to the current fractal type.</summary>
    public bool IsFractal => _fractalType is FractalType.FBm or FractalType.Ridged or FractalType.PingPong;

    /// <summary>Whether the ping-pong strength applies.</summary>
    public bool IsPingPong => _fractalType == FractalType.PingPong;

    /// <summary>Whether the blend factor applies.</summary>
    public bool IsLerp => _blend == LayerBlend.Lerp;

    /// <summary>Every algorithm, for the picker.</summary>
    public static IReadOnlyList<NoiseType> NoiseTypes { get; } = Enum.GetValues<NoiseType>();

    /// <summary>Every fractal mode that makes sense on a layer.</summary>
    public static IReadOnlyList<FractalType> FractalTypes { get; } =
        [FractalType.None, FractalType.FBm, FractalType.Ridged, FractalType.PingPong];

    /// <summary>Every 3D rotation, for the picker.</summary>
    public static IReadOnlyList<RotationType3D> Rotations { get; } = Enum.GetValues<RotationType3D>();

    /// <summary>Every cellular distance metric, for the picker.</summary>
    public static IReadOnlyList<CellularDistanceFunction> CellularDistances { get; } =
        Enum.GetValues<CellularDistanceFunction>();

    /// <summary>Every cellular output, for the picker.</summary>
    public static IReadOnlyList<CellularReturnType> CellularReturns { get; } = Enum.GetValues<CellularReturnType>();

    /// <summary>Every blend mode, for the picker.</summary>
    public static IReadOnlyList<LayerBlend> Blends { get; } = Enum.GetValues<LayerBlend>();

    /// <summary>Builds a generator from the current settings.</summary>
    public NoiseGenerator BuildGenerator() => new(_seed)
    {
        Frequency = _frequency,
        NoiseType = _noiseType,
        RotationType3D = _rotation,
        FractalType = _fractalType,
        Octaves = _octaves,
        Lacunarity = _lacunarity,
        Gain = _gain,
        WeightedStrength = _weightedStrength,
        PingPongStrength = _pingPongStrength,
        CellularDistanceFunction = _cellularDistance,
        CellularReturnType = _cellularReturn,
        CellularJitter = _cellularJitter,
    };

    /// <summary>Builds the stack layer from the current settings.</summary>
    public NoiseLayer BuildLayer() => new()
    {
        Source = BuildGenerator(),
        Blend = _blend,
        Amplitude = _amplitude,
        Offset = _offset,
        BlendFactor = _blendFactor,
        FeatureSize = _featureSize,
        Name = _name,
    };

    /// <summary>Deep-copies this layer.</summary>
    public LayerViewModel Clone() => new()
    {
        _name = _name + " copy",
        _isEnabled = _isEnabled,
        _seed = _seed,
        _frequency = _frequency,
        _noiseType = _noiseType,
        _rotation = _rotation,
        _fractalType = _fractalType,
        _octaves = _octaves,
        _lacunarity = _lacunarity,
        _gain = _gain,
        _weightedStrength = _weightedStrength,
        _pingPongStrength = _pingPongStrength,
        _cellularDistance = _cellularDistance,
        _cellularReturn = _cellularReturn,
        _cellularJitter = _cellularJitter,
        _blend = _blend,
        _amplitude = _amplitude,
        _offset = _offset,
        _blendFactor = _blendFactor,
        _featureSize = _featureSize,
    };

    /// <inheritdoc />
    public override string ToString() => $"{(_isEnabled ? string.Empty : "(off) ")}{_name}";

    private bool SetAndNotify<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Set(ref field, value, propertyName))
        {
            return false;
        }

        Raise(nameof(ToString));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
