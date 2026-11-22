using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// The fields a call may write, member by member: those of its receiver and the statics its callee's body
/// assigns, with the bodies it calls on the same receiver or statically. Purity Requirement 3 lets a callee
/// that writes nothing External touch nothing else, so any other effect, or a <c>this</c> that leaves its
/// receiver position, makes the answer null: the call may write whatever it can reach.
/// </summary>
internal sealed class MemberWrites(FieldEffects effects)
{
    public static readonly IReadOnlySet<Field> Nothing = new HashSet<Field>();

    private readonly Dictionary<Method, IReadOnlySet<Field>?> closures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Method, (HashSet<Field> Fields, List<Method> Callees)?> bodies = new(ReferenceEqualityComparer.Instance);

    public IReadOnlySet<Field>? Of(object node)
    {
        if(!effects.MayWrite(node))
        {
            return Nothing;
        }

        return node is InvocationExpression call && Callee(call) is { } callee ? Closure(callee) : null;
    }

    private static Method? Callee(InvocationExpression call)
    {
        if(call.Method is not { } selected || selected.Modifiers.IsAsync
            || TypeInitializer.TriggeredBy(call).Any(trigger => TypeInitializer.Of(trigger.Type).Initializers().Any()))
        {
            return null;
        }

        return selected.SourceDeclaration ?? selected;
    }

    private IReadOnlySet<Field>? Closure(Method start)
    {
        if(closures.TryGetValue(start, out var known))
        {
            return known;
        }

        var written = new HashSet<Field>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<Method>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Method>();
        pending.Push(start);
        while(pending.TryPop(out var method))
        {
            if(!visited.Add(method))
            {
                continue;
            }

            if(Body(method) is not var (fields, callees))
            {
                closures[start] = null;
                return null;
            }

            written.UnionWith(fields);
            foreach(var callee in callees)
            {
                pending.Push(callee);
            }
        }

        closures[start] = written;
        return written;
    }

    private (HashSet<Field> Fields, List<Method> Callees)? Body(Method method)
    {
        if(!bodies.TryGetValue(method, out var known))
        {
            known = Read(method);
            bodies[method] = known;
        }

        return known;
    }

    private (HashSet<Field> Fields, List<Method> Callees)? Read(Method method)
    {
        if(method.Body is null || method.Modifiers.IsAsync || method is { DeclaringType: AbstractType, Modifiers.IsStatic: false })
        {
            return null;
        }

        var fields = new HashSet<Field>(ReferenceEqualityComparer.Instance);
        var callees = new List<Method>();
        var receivers = new HashSet<ThisExpression>(ReferenceEqualityComparer.Instance);
        var nodes = BodyWalker.DescendantsAndSelf(method.Body).ToList();
        foreach(var node in nodes)
        {
            if(node is MemberAccessExpression { Receiver: ThisExpression receiver })
            {
                _ = receivers.Add(receiver);
            }

            var kept = node switch
            {
                AsyncBlockExpression or AsyncExpression => false,
                AssignmentExpression assignment => !effects.MayWrite(assignment) && Deconstructions.Pairs(assignment).All(pair => Target(pair.Target, fields)),
                CompoundAssignmentExpression compound => !effects.MayWrite(compound) && Target(compound.Target, fields),
                InvocationExpression call => Called(call, fields, callees, receivers),
                _ => !effects.MayWrite(node),
            };
            if(!kept)
            {
                return null;
            }
        }

        return nodes.OfType<ThisExpression>().All(receivers.Contains) ? (fields, callees) : null;
    }

    private bool Called(InvocationExpression call, HashSet<Field> fields, List<Method> callees, HashSet<ThisExpression> receivers)
    {
        var parameters = call.Parameters;
        for(var i = 0; i < call.Arguments.Count; i++)
        {
            if(parameters is not null && i < parameters.Count && parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out
                && !Target(call.Arguments[i], fields))
            {
                return false;
            }
        }

        var receiver = ThisTiers.ReceiverOf(call);
        if(receiver is ThisExpression self)
        {
            _ = receivers.Add(self);
        }

        if(receiver is null or ThisExpression && Callee(call) is { } callee)
        {
            callees.Add(callee);
            return true;
        }

        return !effects.MayWrite(call);
    }

    private static bool Target(Expression target, HashSet<Field> fields)
    {
        var field = target switch
        {
            FieldReferenceExpression reference => reference.Field,
            MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult own } => own.Field,
            MemberAccessExpression { Member: FieldResult { Field.Modifiers.IsStatic: true } shared } => shared.Field,
            _ => null,
        };
        if(field is not null)
        {
            _ = fields.Add(field);
            return true;
        }

        return target is LocalReferenceExpression or IndexExpression or PropertyReferenceExpression or MemberAccessExpression { Member: PropertyResult };
    }
}