using System;

namespace Tedd.FastNoise;

/// <summary>
/// Everything an accelerator needs to reproduce a 2D fill, with no reference back to the generator.
/// </summary>
/// <remarks>
/// Flattened deliberately: an accelerator may run the fill on another device, in another process,
/// or on a queue, so it gets a self-contained value rather than a live object.
/// </remarks>
public readonly record struct NoiseFillRequest2D
{
    /// <summary>Seed of the first octave.</summary>
    public required int Seed { get; init; }

    /// <summary>Cycles per world unit for the first octave.</summary>
    public required float Frequency { get; init; }

    /// <summary>Algorithm producing each octave.</summary>
    public required NoiseType NoiseType { get; init; }

    /// <summary>How octaves combine.</summary>
    public required FractalType FractalType { get; init; }

    /// <summary>Octaves to run. Already reduced by <see cref="LodPolicy"/> if one is active.</summary>
    public required int Octaves { get; init; }

    /// <summary>Frequency multiplier between octaves.</summary>
    public required float Lacunarity { get; init; }

    /// <summary>Amplitude multiplier between octaves.</summary>
    public required float Gain { get; init; }

    /// <summary>Per-octave amplitude bias from the previous octave's value.</summary>
    public float WeightedStrength { get; init; }

    /// <summary>Folds per octave for <see cref="FractalType.PingPong"/>.</summary>
    public float PingPongStrength { get; init; }

    /// <summary>Reciprocal of the summed octave amplitudes.</summary>
    public required float FractalBounding { get; init; }

    /// <summary>Distance metric for cellular noise.</summary>
    public CellularDistanceFunction CellularDistance { get; init; }

    /// <summary>Output selection for cellular noise.</summary>
    public CellularReturnType CellularReturn { get; init; }

    /// <summary>Feature-point displacement for cellular noise.</summary>
    public float CellularJitter { get; init; }

    /// <summary>Amplitude multiplier for the final octave, in (0, 1].</summary>
    public float LastOctaveFade { get; init; }

    /// <summary>Where to sample and how densely.</summary>
    public required GridRegion2D Region { get; init; }
}

/// <summary>Everything an accelerator needs to reproduce a 3D fill.</summary>
public readonly record struct NoiseFillRequest3D
{
    /// <summary>Seed of the first octave.</summary>
    public required int Seed { get; init; }

    /// <summary>Cycles per world unit for the first octave.</summary>
    public required float Frequency { get; init; }

    /// <summary>Algorithm producing each octave.</summary>
    public required NoiseType NoiseType { get; init; }

    /// <summary>Domain rotation applied before sampling.</summary>
    public required RotationType3D RotationType3D { get; init; }

    /// <summary>How octaves combine.</summary>
    public required FractalType FractalType { get; init; }

    /// <summary>Octaves to run. Already reduced by <see cref="LodPolicy"/> if one is active.</summary>
    public required int Octaves { get; init; }

    /// <summary>Frequency multiplier between octaves.</summary>
    public required float Lacunarity { get; init; }

    /// <summary>Amplitude multiplier between octaves.</summary>
    public required float Gain { get; init; }

    /// <summary>Per-octave amplitude bias from the previous octave's value.</summary>
    public float WeightedStrength { get; init; }

    /// <summary>Folds per octave for <see cref="FractalType.PingPong"/>.</summary>
    public float PingPongStrength { get; init; }

    /// <summary>Reciprocal of the summed octave amplitudes.</summary>
    public required float FractalBounding { get; init; }

    /// <summary>Distance metric for cellular noise.</summary>
    public CellularDistanceFunction CellularDistance { get; init; }

    /// <summary>Output selection for cellular noise.</summary>
    public CellularReturnType CellularReturn { get; init; }

    /// <summary>Feature-point displacement for cellular noise.</summary>
    public float CellularJitter { get; init; }

    /// <summary>Amplitude multiplier for the final octave, in (0, 1].</summary>
    public float LastOctaveFade { get; init; }

    /// <summary>Where to sample and how densely.</summary>
    public required GridRegion3D Region { get; init; }
}

/// <summary>
/// A device that can produce a fill faster than the CPU, given a large enough request.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by the optional <c>Tedd.FastNoise.Gpu</c> package. The core library never requires
/// one: with no accelerator registered, <see cref="NoiseBackend.Gpu"/> silently means
/// <see cref="NoiseBackend.Parallel"/>.
/// </para>
/// <para>
/// <b>Implementers must produce the same values the CPU does.</b> Not approximately -- exactly.
/// A world generated on a machine with a GPU and a machine without has to be the same world, or
/// the feature is a liability. The test suite runs the shipped accelerator against the CPU path
/// sample for sample.
/// </para>
/// <para>
/// <see cref="TryFill2D"/> returning <see langword="false"/> is a normal outcome, not an error:
/// it is how an accelerator declines work it cannot do or would do badly, and the caller then runs
/// the CPU path. Throw only for genuine faults.
/// </para>
/// </remarks>
public interface INoiseAccelerator
{
    /// <summary>Whether the device is present and usable right now.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Below this many samples, the caller should not bother asking.
    /// </summary>
    /// <remarks>
    /// Dispatch and readback cost the same whether the fill is a hundred samples or a million, so
    /// small fills are always faster on the CPU. This is where the accelerator declares its own
    /// break-even point.
    /// </remarks>
    int MinimumSampleCount { get; }

    /// <summary>Attempts a 2D fill.</summary>
    /// <param name="destination">Receives <c>request.Region.SampleCount</c> values, X-fastest.</param>
    /// <param name="request">What to generate.</param>
    /// <returns><see langword="true"/> if <paramref name="destination"/> was filled; <see langword="false"/> to fall back to the CPU.</returns>
    bool TryFill2D(Span<float> destination, in NoiseFillRequest2D request);

    /// <summary>Attempts a 3D fill.</summary>
    /// <param name="destination">Receives <c>request.Region.SampleCount</c> values, X-fastest then Y then Z.</param>
    /// <param name="request">What to generate.</param>
    /// <returns><see langword="true"/> if <paramref name="destination"/> was filled; <see langword="false"/> to fall back to the CPU.</returns>
    bool TryFill3D(Span<float> destination, in NoiseFillRequest3D request);
}

/// <summary>The process-wide accelerator registry.</summary>
/// <remarks>
/// Register one at startup and every <see cref="NoiseBackend.Gpu"/> fill in the process will try
/// it. Nothing else in the library holds device state, so this stays a single static slot rather
/// than a dependency threaded through every call.
/// </remarks>
public static class NoiseAccelerator
{
    /// <summary>
    /// The accelerator <see cref="NoiseBackend.Gpu"/> fills route to, or <see langword="null"/> for none.
    /// </summary>
    /// <remarks>
    /// Set once during startup. Replacing it while fills are running is not synchronised; a fill
    /// already in flight may finish against the previous value.
    /// </remarks>
    public static INoiseAccelerator? Current { get; set; }

    /// <summary>The registered accelerator if it is available and worth using for this many samples.</summary>
    /// <param name="sampleCount">Size of the fill being considered.</param>
    internal static INoiseAccelerator? For(int sampleCount)
    {
        INoiseAccelerator? accelerator = Current;
        return accelerator is not null && accelerator.IsAvailable && sampleCount >= accelerator.MinimumSampleCount
            ? accelerator
            : null;
    }
}
