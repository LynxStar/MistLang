using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

/// <summary>
/// For each local function under a body root, the enclosing locals its body reads without declaring,
/// together with those of every local function it calls or names, to a fixpoint. Where a lambda's
/// captured reads are judged at the point it is created, a local function's are judged at every entry
/// into it -- a call, or a conversion of its name to a function value -- because its name is hoisted
/// and a call may precede the declaration. The set is flow-insensitive within a body: a function that
/// assigns a captured local before reading it still counts as reading it, which refuses a call made
/// while that local is unassigned. A write target is not a read -- an assignment's local, a spread
/// assignment's, the struct local a field is written through -- exactly the targets the rule's own
/// Written counts as written; and only a bare reference enters a local function, since it has no
/// declaring type to be reached through.
/// </summary>
internal static class LocalFunctionReads
{
    public static IReadOnlyDictionary<Method, IReadOnlySet<LocalVariable>> Of(object root)
    {
        var functions = BodyWalker.DescendantsAndSelf(root).OfType<LocalFunctionStatement>().ToList();
        var direct = new Dictionary<Method, HashSet<LocalVariable>>(ReferenceEqualityComparer.Instance);
        var entered = new Dictionary<Method, HashSet<Method>>(ReferenceEqualityComparer.Instance);

        foreach(var function in functions)
        {
            var inside = Captures.Inside(function);
            var own = Captures.OwnNames(function.Parameters, inside);
            var written = Written(inside);

            direct[function.Function] = new HashSet<LocalVariable>(
                inside.OfType<LocalReferenceExpression>()
                    .Where(reference => !written.Contains(reference) && !own.Contains(reference.Local))
                    .Select(reference => reference.Local),
                ReferenceEqualityComparer.Instance);

            entered[function.Function] = new HashSet<Method>(
                inside.Select(Entered).OfType<Method>(), ReferenceEqualityComparer.Instance);
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
                        direct[function].UnionWith(calleeReads);
                        changed |= direct[function].Count != before;
                    }
                }
            }
        }

        var reads = new Dictionary<Method, IReadOnlySet<LocalVariable>>(ReferenceEqualityComparer.Instance);

        foreach(var (function, locals) in direct)
        {
            reads[function] = locals;
        }

        return reads;
    }

    private static HashSet<LocalReferenceExpression> Written(IReadOnlyList<object> inside)
    {
        var written = new HashSet<LocalReferenceExpression>(ReferenceEqualityComparer.Instance);

        foreach(var assignment in inside.OfType<AssignmentExpression>().Where(assignment => !assignment.IsCoalescing))
        {
            var target = assignment.Target switch
            {
                SpreadExpression { Operand: LocalReferenceExpression spread } => spread,
                MemberAccessExpression { Receiver: LocalReferenceExpression holder } when FlowGraphBuilder.IsValueLocal(holder) => holder,
                LocalReferenceExpression local => local,
                _ => null,
            };

            if(target is not null)
            {
                _ = written.Add(target);
            }
        }

        return written;
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