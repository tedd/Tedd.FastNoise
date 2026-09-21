// Compiles immutable procedural DAGs into straight-line typed delegates and validates buffer boundaries.
// Shared expressions become locals, so independent outputs reuse arithmetic instead of reinterpreting nodes per sample.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tedd.FastNoise.Procedural;

/// <summary>A reusable, immutable procedural program with explicit ordered arithmetic.</summary>
/// <remarks>Execution is thread-safe and generated execution is allocation-free. Compilation generates dynamic CPU code when
/// supported; NativeAOT uses a scalar interpreter with pooled scratch space and reports this through <see cref="UsesDynamicCode"/>.
/// Inputs and outputs use doubles as a lossless wire representation for float, 32-bit integers and Boolean.
/// Nodes retain their declared arithmetic precision. Dynamic branches may evaluate both pure dependency arms.</remarks>
public sealed class CompiledProceduralGraph
{
    private delegate void Executor(ReadOnlySpan<double> inputs, Span<double> outputs);
    private readonly Executor _execute;
    private readonly Executor? _executeWide;
    private readonly ProceduralNode[] _nodes;
    private readonly int[] _outputs;
    private readonly ProceduralNoise?[] _noise;

    internal CompiledProceduralGraph(ProceduralNode[] nodes, int[] outputs, int builderNodeCount)
    {
        _nodes = nodes; _outputs = outputs;
        Nodes = Array.AsReadOnly(nodes); Outputs = Array.AsReadOnly(outputs);
        BuilderNodeCount = builderNodeCount;
        InputCount = nodes.Where(n => n.Operation == ProceduralOperation.Input).Select(n => n.InputIndex + 1).DefaultIfEmpty().Max();
        UsesDynamicCode = RuntimeFeature.IsDynamicCodeSupported;
        _noise = nodes.Select(n => n.NoiseRequest is { } request ? new ProceduralNoise(request) : null).ToArray();
        _execute = UsesDynamicCode ? Compile() : EvaluateInterpreted;
        _executeWide = UsesDynamicCode && Vector.IsHardwareAccelerated ? CompileWide() : null;
    }

    /// <summary>Optimized, compacted, topologically ordered instructions shared by all backends.</summary>
    public IReadOnlyList<ProceduralNode> Nodes { get; }
    /// <summary>Node indices written to output slots, in caller-declared order.</summary>
    public IReadOnlyList<int> Outputs { get; }
    /// <summary>Minimum input record width, including gaps in numbered inputs.</summary>
    public int InputCount { get; }
    /// <summary>Output record width.</summary>
    public int OutputCount => _outputs.Length;
    /// <summary>Builder node count before unreachable-node elimination.</summary>
    public int BuilderNodeCount { get; }
    /// <summary>Whether CPU execution uses a generated delegate rather than the AOT interpreter fallback.</summary>
    public bool UsesDynamicCode { get; }
    /// <summary>Whether bulk execution has a full-width generated SIMD path.</summary>
    public bool IsVectorised => _executeWide is not null;

    /// <summary>Evaluates one record; generated CPU code neither allocates nor traverses the DAG.</summary>
    public void Evaluate(ReadOnlySpan<double> inputs, Span<double> outputs)
    {
        if (inputs.Length < InputCount) throw new ArgumentException("Input record is too short.", nameof(inputs));
        if (outputs.Length < OutputCount) throw new ArgumentException("Output record is too short.", nameof(outputs));
        _execute(inputs, outputs);
    }

    /// <summary>Evaluates tightly interleaved records; inputs and outputs must not overlap.</summary>
    /// <remarks>Scalar disables SIMD. Other backend selections use the generated CPU SIMD path when
    /// available; this method does not schedule worker threads or GPU work. Explicit GPU recording lives
    /// in Tedd.FastNoise.Gpu, allowing callers to retain generated data on the device.</remarks>
    public void Fill(ReadOnlySpan<double> inputs, Span<double> outputs, int sampleCount, NoiseBackend backend = NoiseBackend.Auto)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        if (inputs.Length < checked(sampleCount * InputCount)) throw new ArgumentException("Input buffer is too short.", nameof(inputs));
        if (outputs.Length < checked(sampleCount * OutputCount)) throw new ArgumentException("Output buffer is too short.", nameof(outputs));
        if (inputs.Overlaps(outputs)) throw new ArgumentException("Bulk input and output buffers must not overlap.", nameof(outputs));
        int i = 0;
        if (backend != NoiseBackend.Scalar && _executeWide is not null)
            for (; i <= sampleCount - Vector<float>.Count; i += Vector<float>.Count)
                _executeWide(inputs.Slice(i * InputCount), outputs.Slice(i * OutputCount));
        for (; i < sampleCount; i++)
            _execute(inputs.Slice(i * InputCount, InputCount), outputs.Slice(i * OutputCount, OutputCount));
    }

    private Executor CompileWide()
    {
        var inputs = Expression.Parameter(typeof(ReadOnlySpan<double>), "inputs");
        var outputs = Expression.Parameter(typeof(Span<double>), "outputs");
        var locals = _nodes.Select(n => Expression.Variable(ProceduralWideRuntime.ClrType(n.Type), $"v{n.Id}")).ToArray();
        List<Expression> body = [];
        foreach (var node in _nodes)
        {
            Expression value = node.Operation == ProceduralOperation.Input
                ? ProceduralWideRuntime.Read(inputs, node.InputIndex, InputCount, node.Type)
                : ProceduralWideRuntime.Emit(node, id => locals[id], id => _nodes[id].Type);
            body.Add(Expression.Assign(locals[node.Id], value));
        }
        for (int i = 0; i < _outputs.Length; i++)
            body.Add(ProceduralRuntime.Call(typeof(ProceduralWideRuntime), nameof(ProceduralWideRuntime.Write),
                outputs, Expression.Constant(i), Expression.Constant(OutputCount),
                ProceduralWideRuntime.ToOutput(locals[_outputs[i]], _nodes[_outputs[i]].Type)));
        return Expression.Lambda<Executor>(Expression.Block(locals, body), inputs, outputs).Compile();
    }

    private Executor Compile()
    {
        var inputs = Expression.Parameter(typeof(ReadOnlySpan<double>), "inputs");
        var outputs = Expression.Parameter(typeof(Span<double>), "outputs");
        var locals = _nodes.Select(n => Expression.Variable(ProceduralRuntime.ClrType(n.Type), $"n{n.Id}")).ToArray();
        List<Expression> body = [];
        foreach (var node in _nodes)
        {
            Expression value = node.Operation == ProceduralOperation.Input
                ? ProceduralRuntime.FromBoundary(ProceduralRuntime.Call(typeof(ProceduralRuntime), nameof(ProceduralRuntime.Read), inputs, Expression.Constant(node.InputIndex)), node.Type)
                : ProceduralRuntime.Emit(node, id => locals[id]);
            body.Add(Expression.Assign(locals[node.Id], value));
        }
        for (int i = 0; i < _outputs.Length; i++)
            body.Add(ProceduralRuntime.Call(typeof(ProceduralRuntime), nameof(ProceduralRuntime.Write),
                outputs, Expression.Constant(i), ProceduralRuntime.ToBoundary(locals[_outputs[i]])));
        return Expression.Lambda<Executor>(Expression.Block(locals, body), inputs, outputs).Compile();
    }

    internal void EvaluateInterpreted(ReadOnlySpan<double> inputs, Span<double> outputs)
    {
        double[] scratch = ArrayPool<double>.Shared.Rent(_nodes.Length);
        try
        {
            foreach (var node in _nodes)
            {
                scratch[node.Id] = node.Operation == ProceduralOperation.Input
                    ? ProceduralRuntime.ConvertBoundary(inputs[node.InputIndex], node.Type)
                    : ProceduralRuntime.EvaluateNode(node, node.A < 0 ? 0 : scratch[node.A], node.B < 0 ? 0 : scratch[node.B],
                        node.C < 0 ? 0 : scratch[node.C], node.A < 0 ? node.Type : _nodes[node.A].Type, _noise[node.Id]);
            }
            for (int i = 0; i < _outputs.Length; i++) outputs[i] = scratch[_outputs[i]];
        }
        finally { ArrayPool<double>.Shared.Return(scratch); }
    }
}
