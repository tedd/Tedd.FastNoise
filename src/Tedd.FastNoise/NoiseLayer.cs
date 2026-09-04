using System;

namespace Tedd.FastNoise;

/// <summary>How a layer combines with everything stacked below it.</summary>
public enum LayerBlend
{
    /// <summary>Add. The usual choice for stacking terrain features.</summary>
    Add = 0,

    /// <summary>Subtract from the accumulator. Carving.</summary>
    Subtract = 1,

    /// <summary>
    /// Multiply into the accumulator. Masking: a layer that is near zero in places suppresses
    /// everything below it there.
    /// </summary>
    Multiply = 2,

    /// <summary>Keep whichever is smaller. Clips the accumulator down to this layer's shape.</summary>
    Min = 3,

    /// <summary>Keep whichever is larger. Punches this layer's shape up through the accumulator.</summary>
    Max = 4,

    /// <summary>Discard the accumulator and start again from this layer.</summary>
    Replace = 5,

    /// <summary>Interpolate toward this layer by <see cref="NoiseLayer.BlendFactor"/>.</summary>
    Lerp = 6,
}

/// <summary>
/// One noise source in a <see cref="NoiseStack"/>, with the amplitude, blend and detail budget
/// that place it in the stack.
/// </summary>
/// <remarks>
/// <para>
/// The intended shape of a world generator: a low-frequency continental layer, a mountain layer
/// blended over it, a detail layer added on top, a mask layer multiplied in to keep the detail off
/// the ocean floor. Each is an independently tunable <see cref="NoiseGenerator"/>; the stack is
/// what fuses them into one pass.
/// </para>
/// <para>
/// Layers are immutable once constructed. Rebuild the stack to change one.
/// </para>
/// </remarks>
public sealed class NoiseLayer
{
    /// <summary>The noise this layer contributes.</summary>
    public required NoiseGenerator Source { get; init; }

    /// <summary>How this layer combines with the layers below. Ignored on the first evaluated layer.</summary>
    public LayerBlend Blend { get; init; } = LayerBlend.Add;

    /// <summary>Multiplier applied to this layer's output before blending.</summary>
    public float Amplitude { get; init; } = 1f;

    /// <summary>Constant added to this layer's output after <see cref="Amplitude"/> and before blending.</summary>
    /// <remarks>
    /// Useful with <see cref="LayerBlend.Multiply"/>: a mask that swings [-1, 1] scales the
    /// accumulator by a negative number half the time, which is rarely what anyone means. An
    /// amplitude of 0.5 with an offset of 0.5 maps it to [0, 1] and it behaves like a mask.
    /// </remarks>
    public float Offset { get; init; }

    /// <summary>Interpolation weight for <see cref="LayerBlend.Lerp"/>, where 0 keeps the accumulator and 1 takes this layer.</summary>
    public float BlendFactor { get; init; } = 0.5f;

    /// <summary>
    /// The size in world units of the smallest feature this layer is responsible for, or 0 for
    /// "always evaluate".
    /// </summary>
    /// <remarks>
    /// The level-of-detail control at layer granularity. A surface-scatter layer whose features are
    /// two blocks across contributes nothing to a view sampled every 500 units, and with
    /// <see cref="LodPolicy.CullLayers"/> enabled it is skipped entirely rather than evaluated and
    /// aliased. This is how a planet-scale view costs a fraction of a walking-around-on-it view:
    /// the continental layers survive the cull and the surface layers do not.
    /// </remarks>
    public float FeatureSize { get; init; }

    /// <summary>An optional name, for diagnostics. Does not affect output.</summary>
    public string? Name { get; init; }

    /// <summary>Returns a copy of this layer with a different amplitude.</summary>
    /// <param name="amplitude">The new amplitude.</param>
    public NoiseLayer WithAmplitude(float amplitude) => new()
    {
        Source = Source,
        Blend = Blend,
        Amplitude = amplitude,
        Offset = Offset,
        BlendFactor = BlendFactor,
        FeatureSize = FeatureSize,
        Name = Name,
    };

    /// <inheritdoc />
    public override string ToString()
        => $"{Name ?? Source.NoiseType.ToString()} [{Blend} x{Amplitude:0.###}"
            + (FeatureSize > 0f ? $", features >= {FeatureSize:0.###}]" : "]");
}
