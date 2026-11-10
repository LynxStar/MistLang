using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.ProgramAnalysis.Flow;

// A compile holds its bodies' graphs and inferred ownership strongly under one weak entry for its model, ambient on the thread
// while analysis or emission runs so that a consumer handed only a body reaches them. A weak entry per body is a handle every
// collection visits, so only a body outside any compile gets one.
internal sealed class FlowGraphs(Func<object, object?, IReadOnlyList<LocalVariable>, FlowGraph>? build = null)
{
    private static readonly ConditionalWeakTable<ProgramModelClass, FlowGraphs> Compiles = new();

    private static readonly FlowGraphs Unowned = new() { Loose = new() };

    [ThreadStatic]
    private static FlowGraphs? entered;

    private readonly ConcurrentDictionary<object, Body> held = new(ReferenceEqualityComparer.Instance);

    private ConditionalWeakTable<object, Body>? Loose { get; init; }

    public static FlowGraphs Shared => entered ?? Unowned;

    public static IDisposable Enter(ProgramModelClass model)
    {
        return Enter(Compiles.GetValue(model, static _ => new FlowGraphs()));
    }

    public static IDisposable Enter(FlowGraphs graphs)
    {
        var scope = new Scope(entered);
        entered = graphs;
        return scope;
    }

    public FlowGraph GraphOf(object root)
    {
        return Of(root).Graph;
    }

    public LocalOwnership OwnershipOf(object root, Func<object, LocalOwnership> infer)
    {
        var body = Of(root);
        if(body.Ownership is { } known)
        {
            return known;
        }

        var inferred = infer(root);

        return Interlocked.CompareExchange(ref body.Ownership, inferred, null) ?? inferred;
    }

    public void Register(object root)
    {
        _ = Of(root);
    }

    private Body Of(object root)
    {
        if(Known(root) is { } known)
        {
            return known;
        }

        foreach(var node in BodyWalker.DescendantsAndSelf(root))
        {
            switch(node)
            {
                case LambdaExpression lambda when((object?)lambda.Body ?? lambda.Result) is { } body:
                    _ = Built(body, lambda, lambda.Parameters);
                    break;
                case LocalFunctionStatement function:
                    _ = Built(function.Body, function, function.Parameters);
                    break;
                case AsyncBlockExpression asyncBlock:
                    _ = Built(asyncBlock.Body, asyncBlock, []);
                    break;
            }
        }

        return Built(root, null, []);
    }

    private Body Built(object root, object? introducer, IReadOnlyList<LocalVariable> introduced)
    {
        if(Known(root) is { } known)
        {
            return known;
        }

        var body = new Body(build is not null
            ? build(root, introducer, introduced)
            : FlowGraphBuilder.Build(root, introducer, introduced, Nested));

        return Loose is null ? held.GetOrAdd(root, body) : Loose.GetOrAdd(root, body);
    }

    private FlowGraph Nested(object root, object? introducer, IReadOnlyList<LocalVariable> introduced)
    {
        return Built(root, introducer, introduced).Graph;
    }

    private Body? Known(object root)
    {
        if(Loose is null)
        {
            return held.GetValueOrDefault(root);
        }

        return Loose.TryGetValue(root, out var body) ? body : null;
    }

    private sealed class Body(FlowGraph graph)
    {
        public LocalOwnership? Ownership;

        public FlowGraph Graph { get; } = graph;
    }

    private sealed class Scope(FlowGraphs? outer) : IDisposable
    {
        public void Dispose()
        {
            entered = outer;
        }
    }
}