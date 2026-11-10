using System.Runtime.CompilerServices;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Flow;

// Finished bodies also feed inference and cleanup outside analyzer contexts; weak keys share their graphs through emission without retaining models.
internal sealed class FlowGraphs(Func<object, object?, IReadOnlyList<LocalVariable>, FlowGraph>? build = null)
{
    public static readonly FlowGraphs Shared = new();

    private readonly ConditionalWeakTable<object, Lazy<FlowGraph>> graphs = new();

    public FlowGraph GraphOf(object root)
    {
        if(!graphs.TryGetValue(root, out var graph))
        {
            Register(root);
            graph = graphs.GetValue(root, body => Create(body, null, []));
        }

        return graph.Value;
    }

    public void Register(object root)
    {
        if(graphs.TryGetValue(root, out _))
        {
            return;
        }

        foreach(var node in BodyWalker.DescendantsAndSelf(root))
        {
            switch(node)
            {
                case LambdaExpression lambda when((object?)lambda.Body ?? lambda.Result) is { } body:
                    _ = graphs.GetValue(body, value => Create(value, lambda, lambda.Parameters));
                    break;
                case LocalFunctionStatement function:
                    _ = graphs.GetValue(function.Body, value => Create(value, function, function.Parameters));
                    break;
                case AsyncBlockExpression asyncBlock:
                    _ = graphs.GetValue(asyncBlock.Body, value => Create(value, asyncBlock, []));
                    break;
            }
        }

        _ = graphs.GetValue(root, body => Create(body, null, []));
    }

    private Lazy<FlowGraph> Create(object root, object? introducer, IReadOnlyList<LocalVariable> introduced)
    {
        return new Lazy<FlowGraph>(() => build is not null
            ? build(root, introducer, introduced)
            : FlowGraphBuilder.Build(root, introducer, introduced, Nested));
    }

    private FlowGraph Nested(object root, object? introducer, IReadOnlyList<LocalVariable> introduced)
    {
        return graphs.GetValue(root, body => Create(body, introducer, introduced)).Value;
    }
}