// Purpose: compose footprint-aware solid geometry in the portable procedural graph.
// Responsibilities: classify half-open voxel boxes against solid boxes and hollow box shells.
// Design intent: lower to existing arithmetic and Boolean nodes so scalar, SIMD and GPU kernels share occupancy rules.
using System;

namespace Tedd.FastNoise.Procedural;

/// <summary>Reusable geometry expressions for procedural material and occupancy programs.</summary>
public static class ProceduralGeometry
{
    /// <summary>Tests whether two half-open boxes intersect with positive volume.</summary>
    /// <remarks>All coordinates must use the same floating-point type. Empty or reversed intervals do not intersect.
    /// Testing the complete voxel footprint preserves surfaces lying between coarse sample centers.</remarks>
    public static ProceduralValue IntersectsBox(this ProceduralGraph graph,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) minimum,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) maximum,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) boxMinimum,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) boxMaximum)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Validate(minimum.X.Type, minimum, maximum, boxMinimum, boxMaximum);
        return graph.And(graph.And(Axis(minimum.X, maximum.X, boxMinimum.X, boxMaximum.X),
            Axis(minimum.Y, maximum.Y, boxMinimum.Y, boxMaximum.Y)),
            Axis(minimum.Z, maximum.Z, boxMinimum.Z, boxMaximum.Z));

        ProceduralValue Axis(ProceduralValue a, ProceduralValue b, ProceduralValue c, ProceduralValue d) =>
            graph.And(graph.And(graph.LessThan(a, b), graph.LessThan(c, d)),
                graph.And(graph.LessThan(a, d), graph.LessThan(c, b)));
    }

    /// <summary>Tests positive-volume overlap with an outer box after subtracting an inner box.</summary>
    /// <remarks>Bounds are half-open and share one floating-point type. The inner box may touch or extend past
    /// the outer box; an empty inner box removes nothing. Entirely hollow voxel footprints remain empty.</remarks>
    public static ProceduralValue IntersectsBoxShell(this ProceduralGraph graph,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) minimum,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) maximum,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) outerMinimum,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) outerMaximum,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) innerMinimum,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) innerMaximum)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Validate(minimum.X.Type, innerMinimum, innerMaximum, minimum, maximum);
        var intersects = graph.IntersectsBox(minimum, maximum, outerMinimum, outerMaximum);
        // Containment applies to the intersection with the outer solid. Using the original voxel
        // would retain material incorrectly when the inner subtraction reaches an outer boundary.
        var removed = graph.And(graph.And(Axis(minimum.X, maximum.X, outerMinimum.X, outerMaximum.X, innerMinimum.X, innerMaximum.X),
            Axis(minimum.Y, maximum.Y, outerMinimum.Y, outerMaximum.Y, innerMinimum.Y, innerMaximum.Y)),
            Axis(minimum.Z, maximum.Z, outerMinimum.Z, outerMaximum.Z, innerMinimum.Z, innerMaximum.Z));
        return graph.And(intersects, graph.Not(removed));

        ProceduralValue Axis(ProceduralValue a, ProceduralValue b, ProceduralValue c, ProceduralValue d,
            ProceduralValue innerA, ProceduralValue innerB) =>
            graph.And(graph.LessThan(innerA, innerB), graph.And(
                graph.GreaterThanOrEqual(graph.Max(a, c), innerA),
                graph.LessThanOrEqual(graph.Min(b, d), innerB)));
    }

    private static void Validate(ProceduralType type,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) a,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) b,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) c,
        (ProceduralValue X, ProceduralValue Y, ProceduralValue Z) d)
    {
        if (type is not (ProceduralType.Float32 or ProceduralType.Float64)
            || !Same(a) || !Same(b) || !Same(c) || !Same(d))
            throw new ArgumentException("Geometry bounds must share one floating-point type.");

        bool Same((ProceduralValue X, ProceduralValue Y, ProceduralValue Z) value) =>
            value.X.Type == type && value.Y.Type == type && value.Z.Type == type;
    }
}
