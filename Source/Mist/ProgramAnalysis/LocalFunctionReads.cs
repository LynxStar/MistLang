using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Captured storage reads are flow-insensitive within a local function and judged at every call or
/// conversion of its hoisted name. Lambda and async captures contribute when their expression is
/// evaluated; another local function contributes only through an entry into it, to a fixpoint. The
/// shared graph distinguishes reading a field from evaluating its storage receiver and write target.
/// </summary>
internal static class LocalFunctionReads
{
    public static IReadOnlyDictionary<Method, IReadOnlySet<LocalStoragePath>> Of(FlowGraph graph)
    {
        var direct = new Dictionary<Method, HashSet<LocalStoragePath>>(ReferenceEqualityComparer.Instance);
        var entered = new Dictionary<Method, HashSet<Method>>(ReferenceEqualityComparer.Instance);
        var declared = new Dictionary<Method, HashSet<LocalVariable>>(ReferenceEqualityComparer.Instance);

        foreach(var (function, body) in Functions(graph))
        {
            var own = Captures.OwnNames(function.Parameters, Captures.Inside(function));
            var evaluated = Evaluated(body).ToList();
            declared[function.Function] = own;

            direct[function.Function] = evaluated.OfType<Expression>()
                .Select(LocalStoragePath.Of)
                .OfType<LocalStoragePath>()
                .Where(path => !own.Contains(path.Local))
                .ToHashSet();

            entered[function.Function] = new HashSet<Method>(
                evaluated.Select(Entered).OfType<Method>(), ReferenceEqualityComparer.Instance);
        }

        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var (function, callees) in entered)
            {
                foreach(var callee in callees)
                {
                    if(direct.TryGetValue(callee, out var calleeReads))
                    {
                        var before = direct[function].Count;
                        direct[function].UnionWith(calleeReads.Where(path => !declared[function].Contains(path.Local)));
                        changed |= direct[function].Count != before;
                    }
                }
            }
        }

        var reads = new Dictionary<Method, IReadOnlySet<LocalStoragePath>>(ReferenceEqualityComparer.Instance);

        foreach(var (function, locals) in direct)
        {
            reads[function] = locals;
        }

        return reads;
    }

    private static IEnumerable<(LocalFunctionStatement Function, FlowGraph Body)> Functions(FlowGraph graph)
    {
        foreach(var (node, nested) in graph.Nested)
        {
            if(node is LocalFunctionStatement function)
            {
                yield return (function, nested);
            }

            foreach(var descendant in Functions(nested))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<object> Evaluated(FlowGraph graph)
    {
        foreach(var step in graph.Blocks.SelectMany(block => block.Steps))
        {
            if(step.Kind != FlowStepKind.Evaluate || step.IsStorageReceiver)
            {
                continue;
            }

            yield return step.Node;

            if(step.Node is LambdaExpression or AsyncBlockExpression && graph.Nested.TryGetValue(step.Node, out var nested))
            {
                foreach(var node in Evaluated(nested))
                {
                    yield return node;
                }
            }
        }
    }

    public static Method? Entered(object node)
    {
        return node switch
        {
            InvocationExpression { Receiver: MethodReferenceExpression { Method: { DeclaringType: null } callee } } => callee,
            FunctionReferenceExpression { Receiver: null, Method: { DeclaringType: null } named } => named,
            _ => null,
        };
    }
}