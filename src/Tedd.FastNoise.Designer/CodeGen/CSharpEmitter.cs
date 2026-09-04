using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Tedd.FastNoise.Designer.ViewModels;

namespace Tedd.FastNoise.Designer.CodeGen;

/// <summary>
/// Emits the C# that reproduces what the designer is currently previewing.
/// </summary>
/// <remarks>
/// The point of the designer is to stop people transcribing slider values into source by hand and
/// getting one of them wrong. What comes out here is meant to be pasted into a project and run --
/// only settings that differ from the defaults are written, so the output stays readable.
/// </remarks>
public static class CSharpEmitter
{
    /// <summary>Builds the snippet.</summary>
    /// <param name="layers">The layers, in stack order. Disabled layers are skipped.</param>
    /// <param name="lod">The level-of-detail policy in effect.</param>
    /// <param name="preview">The region currently being previewed.</param>
    /// <param name="isVolume">Whether the preview is a 3D volume rather than a 2D map.</param>
    public static string Emit(
        IReadOnlyList<LayerViewModel> layers,
        LodPolicy lod,
        PreviewRegion preview,
        bool isVolume)
    {
        List<LayerViewModel> active = layers.Where(static layer => layer.IsEnabled).ToList();

        StringBuilder code = new();
        code.AppendLine("using Tedd.FastNoise;");
        code.AppendLine();

        if (active.Count == 0)
        {
            code.AppendLine("// No layers are enabled.");
            return code.ToString();
        }

        if (active.Count == 1 && IsPlainAdditive(active[0]))
        {
            EmitSingleGenerator(code, active[0], lod, preview, isVolume);
        }
        else
        {
            EmitStack(code, active, lod, preview, isVolume);
        }

        return code.ToString();
    }

    /// <summary>A single layer with default stack settings needs no stack at all.</summary>
    private static bool IsPlainAdditive(LayerViewModel layer)
        => layer is { Amplitude: 1f, Offset: 0f, FeatureSize: 0f };

    private static void EmitSingleGenerator(
        StringBuilder code, LayerViewModel layer, LodPolicy lod, PreviewRegion preview, bool isVolume)
    {
        code.AppendLine($"var noise = new NoiseGenerator(seed: {layer.Seed})");
        code.AppendLine("{");
        foreach (string line in GeneratorSettings(layer))
        {
            code.AppendLine($"    {line}");
        }

        if (lod.CullOctaves)
        {
            code.AppendLine($"    Lod = {LodExpression(lod)},");
        }

        code.AppendLine("};");
        code.AppendLine();
        EmitFill(code, "noise", preview, isVolume);
    }

    private static void EmitStack(
        StringBuilder code, IReadOnlyList<LayerViewModel> layers, LodPolicy lod, PreviewRegion preview, bool isVolume)
    {
        code.AppendLine(lod.CullOctaves
            ? $"var stack = new NoiseStack {{ Lod = {LodExpression(lod)} }};"
            : "var stack = new NoiseStack();");
        code.AppendLine();

        for (int index = 0; index < layers.Count; index++)
        {
            LayerViewModel layer = layers[index];

            code.AppendLine("stack.Add(new NoiseLayer");
            code.AppendLine("{");
            code.AppendLine($"    Source = new NoiseGenerator(seed: {layer.Seed})");
            code.AppendLine("    {");
            foreach (string line in GeneratorSettings(layer))
            {
                code.AppendLine($"        {line}");
            }

            code.AppendLine("    },");

            // The first layer initialises the accumulator, so its blend is meaningless.
            if (index > 0 && layer.Blend != LayerBlend.Add)
            {
                code.AppendLine($"    Blend = LayerBlend.{layer.Blend},");
            }

            if (index > 0 && layer.Blend == LayerBlend.Lerp)
            {
                code.AppendLine($"    BlendFactor = {Literal(layer.BlendFactor)},");
            }

            if (layer.Amplitude != 1f)
            {
                code.AppendLine($"    Amplitude = {Literal(layer.Amplitude)},");
            }

            if (layer.Offset != 0f)
            {
                code.AppendLine($"    Offset = {Literal(layer.Offset)},");
            }

            if (layer.FeatureSize != 0f)
            {
                code.AppendLine($"    FeatureSize = {Literal(layer.FeatureSize)},");
            }

            code.AppendLine($"    Name = {Quote(layer.Name)},");
            code.AppendLine("});");
            code.AppendLine();
        }

        code.AppendLine("// Compile once and keep it: the compiled stack is immutable, thread-safe,");
        code.AppendLine("// and runs every layer in one pass instead of a buffer per layer.");
        code.AppendLine("var world = stack.Compile();");
        code.AppendLine();
        EmitFill(code, "world", preview, isVolume);
    }

    private static void EmitFill(StringBuilder code, string variable, PreviewRegion preview, bool isVolume)
    {
        if (isVolume)
        {
            code.AppendLine($"var region = new GridRegion3D(");
            code.AppendLine($"    {Literal(preview.OriginX)}, {Literal(preview.OriginY)}, {Literal(preview.OriginZ)},");
            code.AppendLine($"    {preview.Width}, {preview.Height}, {preview.Depth},");
            code.AppendLine($"    step: {Literal(preview.Step)});");
        }
        else
        {
            code.AppendLine($"var region = new GridRegion2D(");
            code.AppendLine($"    {Literal(preview.OriginX)}, {Literal(preview.OriginY)},");
            code.AppendLine($"    {preview.Width}, {preview.Height},");
            code.AppendLine($"    step: {Literal(preview.Step)});");
        }

        code.AppendLine();
        code.AppendLine("var field = new float[region.SampleCount];");
        code.AppendLine($"{variable}.Fill(field, region);");
    }

    /// <summary>Emits only the settings that differ from the defaults.</summary>
    private static IEnumerable<string> GeneratorSettings(LayerViewModel layer)
    {
        NoiseGenerator defaults = new();

        if (layer.NoiseType != defaults.NoiseType)
        {
            yield return $"NoiseType = NoiseType.{layer.NoiseType},";
        }

        if (layer.Frequency != defaults.Frequency)
        {
            yield return $"Frequency = {Literal(layer.Frequency)},";
        }

        if (layer.Rotation != defaults.RotationType3D)
        {
            yield return $"RotationType3D = RotationType3D.{layer.Rotation},";
        }

        if (layer.FractalType != defaults.FractalType)
        {
            yield return $"FractalType = FractalType.{layer.FractalType},";
        }

        if (layer.IsFractal)
        {
            if (layer.Octaves != defaults.Octaves)
            {
                yield return $"Octaves = {layer.Octaves},";
            }

            if (layer.Lacunarity != defaults.Lacunarity)
            {
                yield return $"Lacunarity = {Literal(layer.Lacunarity)},";
            }

            if (layer.Gain != defaults.Gain)
            {
                yield return $"Gain = {Literal(layer.Gain)},";
            }

            if (layer.WeightedStrength != defaults.WeightedStrength)
            {
                yield return $"WeightedStrength = {Literal(layer.WeightedStrength)},";
            }

            if (layer.IsPingPong && layer.PingPongStrength != defaults.PingPongStrength)
            {
                yield return $"PingPongStrength = {Literal(layer.PingPongStrength)},";
            }
        }

        if (layer.IsCellular)
        {
            if (layer.CellularDistance != defaults.CellularDistanceFunction)
            {
                yield return $"CellularDistanceFunction = CellularDistanceFunction.{layer.CellularDistance},";
            }

            if (layer.CellularReturn != defaults.CellularReturnType)
            {
                yield return $"CellularReturnType = CellularReturnType.{layer.CellularReturn},";
            }

            if (layer.CellularJitter != defaults.CellularJitter)
            {
                yield return $"CellularJitter = {Literal(layer.CellularJitter)},";
            }
        }
    }

    private static string LodExpression(LodPolicy lod)
    {
        List<string> overrides = [];

        if (lod.NyquistFactor != 2f)
        {
            overrides.Add($"NyquistFactor = {Literal(lod.NyquistFactor)}");
        }

        if (!lod.FadeLastOctave)
        {
            overrides.Add("FadeLastOctave = false");
        }

        if (lod.CullLayers)
        {
            overrides.Add("CullLayers = true");
        }

        return overrides.Count == 0
            ? "LodPolicy.Automatic"
            : $"LodPolicy.Automatic with {{ {string.Join(", ", overrides)} }}";
    }

    private static string Literal(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.OrdinalIgnoreCase)
            ? text + "f"
            : text + "f";
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

/// <summary>The region the designer is previewing, in the form the emitter needs.</summary>
/// <param name="OriginX">World X of the first sample.</param>
/// <param name="OriginY">World Y of the first sample.</param>
/// <param name="OriginZ">World Z of the first sample.</param>
/// <param name="Width">Samples along X.</param>
/// <param name="Height">Samples along Y.</param>
/// <param name="Depth">Samples along Z.</param>
/// <param name="Step">World units between samples.</param>
public readonly record struct PreviewRegion(
    float OriginX,
    float OriginY,
    float OriginZ,
    int Width,
    int Height,
    int Depth,
    float Step);
