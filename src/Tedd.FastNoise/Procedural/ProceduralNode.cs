// Defines the immutable, typed procedural intermediate representation shared by CPU and GPU backends.
// Explicit precision and ordered operands preserve terrain decisions when specializing mathematical expressions.
using System;
using System.Collections.Generic;

namespace Tedd.FastNoise.Procedural;

/// <summary>Arithmetic precision or logical representation carried by a graph edge.</summary>
public enum ProceduralType
{
    /// <summary>IEEE binary32.</summary>
    Float32,
    /// <summary>IEEE binary64.</summary>
    Float64,
    /// <summary>Signed wrapping 32-bit integer.</summary>
    Int32,
    /// <summary>Unsigned wrapping 32-bit integer.</summary>
    UInt32,
    /// <summary>Boolean; encoded as zero or one at the input/output boundary.</summary>
    Bool,
}

/// <summary>Pure operations available to procedural backends.</summary>
public enum ProceduralOperation
{
    /// <summary>Numbered runtime input.</summary>
    Input,
    /// <summary>Compile-time value.</summary>
    Constant,
    /// <summary>Ordered addition.</summary>
    Add,
    /// <summary>Ordered subtraction.</summary>
    Subtract,
    /// <summary>Ordered multiplication.</summary>
    Multiply,
    /// <summary>Floating-point division.</summary>
    Divide,
    /// <summary>Minimum with IEEE NaN and signed-zero propagation.</summary>
    Min,
    /// <summary>Maximum with IEEE NaN and signed-zero propagation.</summary>
    Max,
    /// <summary>Absolute value.</summary>
    Abs,
    /// <summary>Floor.</summary>
    Floor,
    /// <summary>Square root.</summary>
    Sqrt,
    /// <summary>Power; individual backends may reject unsupported precision.</summary>
    Pow,
    /// <summary>Arithmetic negation.</summary>
    Negate,
    /// <summary>Ordered less-than comparison.</summary>
    LessThan,
    /// <summary>Ordered less-than-or-equal comparison.</summary>
    LessThanOrEqual,
    /// <summary>Equality.</summary>
    Equal,
    /// <summary>Inequality.</summary>
    NotEqual,
    /// <summary>Ordered greater-than comparison.</summary>
    GreaterThan,
    /// <summary>Ordered greater-than-or-equal comparison.</summary>
    GreaterThanOrEqual,
    /// <summary>Boolean conjunction.</summary>
    And,
    /// <summary>Boolean disjunction.</summary>
    Or,
    /// <summary>Boolean negation.</summary>
    Not,
    /// <summary>Integer bitwise conjunction.</summary>
    BitAnd,
    /// <summary>Integer bitwise disjunction.</summary>
    BitOr,
    /// <summary>Integer bitwise exclusive-or.</summary>
    BitXor,
    /// <summary>Integer left shift with count masked to five bits.</summary>
    ShiftLeft,
    /// <summary>Integer right shift with count masked to five bits.</summary>
    ShiftRight,
    /// <summary>Conditional value; dependent graph nodes are pure and may execute eagerly.</summary>
    Select,
    /// <summary>Explicit numeric conversion.</summary>
    Convert,
    /// <summary>Deterministic unsigned 32-bit avalanche hash.</summary>
    Hash,
    /// <summary>Immutable table access with index clamped to the table bounds.</summary>
    Lookup,
    /// <summary>LOD-specialized two-dimensional noise.</summary>
    Noise2D,
    /// <summary>LOD-specialized three-dimensional noise.</summary>
    Noise3D,
}

/// <summary>An immutable node in a topologically ordered procedural program.</summary>
public sealed record ProceduralNode
{
    internal ProceduralNode() { }
    /// <summary>Index in the owning program.</summary>
    public int Id { get; internal init; }
    /// <summary>Value representation.</summary>
    public ProceduralType Type { get; internal init; }
    /// <summary>Operation performed.</summary>
    public ProceduralOperation Operation { get; internal init; }
    /// <summary>First operand, or -1.</summary>
    public int A { get; internal init; } = -1;
    /// <summary>Second operand, or -1.</summary>
    public int B { get; internal init; } = -1;
    /// <summary>Third operand, or -1.</summary>
    public int C { get; internal init; } = -1;
    /// <summary>Constant value; all supported integers are represented exactly.</summary>
    public double Constant { get; internal init; }
    /// <summary>Index of a runtime input.</summary>
    public int InputIndex { get; internal init; } = -1;
    /// <summary>Frozen typed lookup data.</summary>
    public IReadOnlyList<double>? Table { get; internal init; }
    /// <summary>Frozen noise parameters, including original normalization and specialized octave fade.</summary>
    public NoiseFillRequest3D? NoiseRequest { get; internal init; }
}

/// <summary>A typed edge belonging to one mutable graph builder.</summary>
public readonly struct ProceduralValue : IEquatable<ProceduralValue>
{
    internal ProceduralValue(ProceduralGraph graph, int id, ProceduralType type)
    { Graph = graph; Id = id; Type = type; }
    internal ProceduralGraph Graph { get; }
    /// <summary>Builder-local node index.</summary>
    public int Id { get; }
    /// <summary>Value representation.</summary>
    public ProceduralType Type { get; }
    /// <inheritdoc />
    public bool Equals(ProceduralValue other) => ReferenceEquals(Graph, other.Graph) && Id == other.Id;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ProceduralValue value && Equals(value);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Graph, Id);
    /// <summary>Tests graph-edge identity, not runtime numeric equality.</summary>
    public static bool operator ==(ProceduralValue left, ProceduralValue right) => left.Equals(right);
    /// <summary>Tests graph-edge identity, not runtime numeric inequality.</summary>
    public static bool operator !=(ProceduralValue left, ProceduralValue right) => !left.Equals(right);
    /// <summary>Adds matching numeric edges.</summary>
    public static ProceduralValue operator +(ProceduralValue left, ProceduralValue right) => left.Graph.Add(left, right);
    /// <summary>Subtracts matching numeric edges.</summary>
    public static ProceduralValue operator -(ProceduralValue left, ProceduralValue right) => left.Graph.Subtract(left, right);
    /// <summary>Multiplies matching numeric edges.</summary>
    public static ProceduralValue operator *(ProceduralValue left, ProceduralValue right) => left.Graph.Multiply(left, right);
    /// <summary>Divides matching floating-point edges.</summary>
    public static ProceduralValue operator /(ProceduralValue left, ProceduralValue right) => left.Graph.Divide(left, right);
    /// <summary>Negates a signed numeric edge.</summary>
    public static ProceduralValue operator -(ProceduralValue value) => value.Graph.Negate(value);
}
