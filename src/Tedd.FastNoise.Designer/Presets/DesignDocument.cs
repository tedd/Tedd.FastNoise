using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tedd.FastNoise.Designer.Rendering;
using Tedd.FastNoise.Designer.ViewModels;

namespace Tedd.FastNoise.Designer.Presets;

/// <summary>
/// A saved design: the layer stack plus the view it was being looked at through.
/// </summary>
/// <remarks>
/// <para>
/// Plain JSON with every value spelled out, rather than serialising the view models directly. A
/// design someone tuned for an hour should survive a refactor of the designer, and reading the file
/// should not require the app.
/// </para>
/// <para>
/// Enums are written as names, so a file stays readable and does not silently change meaning if an
/// enum ever gains a member in the middle.
/// </para>
/// </remarks>
public sealed class DesignDocument
{
    /// <summary>Format version, so a future reader can tell what it is looking at.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The layers, coarsest first.</summary>
    public List<LayerDocument> Layers { get; set; } = [];

    /// <summary>The view settings the design was saved with.</summary>
    public ViewDocument View { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Writes the design to disk.</summary>
    /// <param name="path">Destination file.</param>
    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    /// <summary>Reads a design from disk.</summary>
    /// <param name="path">Source file.</param>
    /// <exception cref="InvalidDataException">The file is not a design, or is empty.</exception>
    public static DesignDocument Load(string path)
        => JsonSerializer.Deserialize<DesignDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"'{Path.GetFileName(path)}' does not contain a design.");
}

/// <summary>One layer, as stored.</summary>
public sealed class LayerDocument
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = "layer";

    /// <summary>Whether the layer takes part.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Seed.</summary>
    public int Seed { get; set; } = 1337;

    /// <summary>Cycles per world unit.</summary>
    public float Frequency { get; set; } = 0.01f;

    /// <summary>Algorithm.</summary>
    public NoiseType NoiseType { get; set; } = NoiseType.OpenSimplex2;

    /// <summary>3D domain rotation.</summary>
    public RotationType3D Rotation { get; set; }

    /// <summary>How octaves combine.</summary>
    public FractalType FractalType { get; set; } = FractalType.FBm;

    /// <summary>Octave count.</summary>
    public int Octaves { get; set; } = 4;

    /// <summary>Frequency multiplier between octaves.</summary>
    public float Lacunarity { get; set; } = 2f;

    /// <summary>Amplitude multiplier between octaves.</summary>
    public float Gain { get; set; } = 0.5f;

    /// <summary>Per-octave amplitude bias.</summary>
    public float WeightedStrength { get; set; }

    /// <summary>Folds per octave for ping-pong fractals.</summary>
    public float PingPongStrength { get; set; } = 2f;

    /// <summary>Cellular distance metric.</summary>
    public CellularDistanceFunction CellularDistance { get; set; } = CellularDistanceFunction.EuclideanSq;

    /// <summary>Cellular output selection.</summary>
    public CellularReturnType CellularReturn { get; set; } = CellularReturnType.Distance;

    /// <summary>Cellular feature-point displacement.</summary>
    public float CellularJitter { get; set; } = 1f;

    /// <summary>How the layer folds into the ones below.</summary>
    public LayerBlend Blend { get; set; } = LayerBlend.Add;

    /// <summary>Multiplier applied before blending.</summary>
    public float Amplitude { get; set; } = 1f;

    /// <summary>Constant added before blending.</summary>
    public float Offset { get; set; }

    /// <summary>Interpolation weight for the Lerp blend.</summary>
    public float BlendFactor { get; set; } = 0.5f;

    /// <summary>Smallest feature in world units, for level-of-detail culling.</summary>
    public float FeatureSize { get; set; }

    /// <summary>Captures a layer view model.</summary>
    /// <param name="layer">The layer to capture.</param>
    public static LayerDocument From(LayerViewModel layer) => new()
    {
        Name = layer.Name,
        IsEnabled = layer.IsEnabled,
        Seed = layer.Seed,
        Frequency = layer.Frequency,
        NoiseType = layer.NoiseType,
        Rotation = layer.Rotation,
        FractalType = layer.FractalType,
        Octaves = layer.Octaves,
        Lacunarity = layer.Lacunarity,
        Gain = layer.Gain,
        WeightedStrength = layer.WeightedStrength,
        PingPongStrength = layer.PingPongStrength,
        CellularDistance = layer.CellularDistance,
        CellularReturn = layer.CellularReturn,
        CellularJitter = layer.CellularJitter,
        Blend = layer.Blend,
        Amplitude = layer.Amplitude,
        Offset = layer.Offset,
        BlendFactor = layer.BlendFactor,
        FeatureSize = layer.FeatureSize,
    };

    /// <summary>Rebuilds a layer view model.</summary>
    public LayerViewModel ToViewModel() => new()
    {
        Name = Name,
        IsEnabled = IsEnabled,
        Seed = Seed,
        Frequency = Frequency,
        NoiseType = NoiseType,
        Rotation = Rotation,
        FractalType = FractalType,
        Octaves = Math.Max(1, Octaves),
        Lacunarity = Lacunarity,
        Gain = Gain,
        WeightedStrength = WeightedStrength,
        PingPongStrength = PingPongStrength,
        CellularDistance = CellularDistance,
        CellularReturn = CellularReturn,
        CellularJitter = CellularJitter,
        Blend = Blend,
        Amplitude = Amplitude,
        Offset = Offset,
        BlendFactor = BlendFactor,
        FeatureSize = FeatureSize,
    };
}

/// <summary>The preview settings, as stored.</summary>
public sealed class ViewDocument
{
    /// <summary>Which preview was showing.</summary>
    public PreviewMode Mode { get; set; } = PreviewMode.Map2D;

    /// <summary>Colour mapping.</summary>
    public RampKind Ramp { get; set; } = RampKind.Terrain;

    /// <summary>World X of the first sample.</summary>
    public float OriginX { get; set; }

    /// <summary>World Y of the first sample.</summary>
    public float OriginY { get; set; }

    /// <summary>World Z of the first sample.</summary>
    public float OriginZ { get; set; }

    /// <summary>World units between samples.</summary>
    public float Step { get; set; } = 1f;

    /// <summary>Edge length in samples of the 2D preview.</summary>
    public int Resolution { get; set; } = 384;

    /// <summary>Edge length in samples of the volume preview.</summary>
    public int VolumeResolution { get; set; } = 40;

    /// <summary>Solid threshold.</summary>
    public float Threshold { get; set; }

    /// <summary>Vertical exaggeration of the heightmap mesh.</summary>
    public float HeightScale { get; set; } = 0.25f;

    /// <summary>Whether the 2D view draws a solid/empty mask.</summary>
    public bool ShowThresholdMask { get; set; }

    /// <summary>Whether detail below the sample grid is dropped.</summary>
    public bool LodEnabled { get; set; }

    /// <summary>Whether whole layers are culled by feature size.</summary>
    public bool LodCullLayers { get; set; } = true;

    /// <summary>Whether the finest surviving octave fades in.</summary>
    public bool LodFadeLastOctave { get; set; } = true;

    /// <summary>Samples required per wavelength for an octave to survive.</summary>
    public float NyquistFactor { get; set; } = 2f;
}
