using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// A receiver's fields carry facts only while nothing outside the body can reach its object (flow-sensitive
/// refinement inference Requirement 3.5), so each use of it must be one whose writes the flow sees: a field
/// access, a call on it whose <see cref="MemberWrites"/> are known and that hands back no reference into it, a
/// lend or a move, or a write of the local. Any other use, or a capture, may hand the object to code that writes
/// it later. Only <c>owned</c> promises the object is the body's alone: a <c>borrowed</c> binding may be taken
/// from a field or a fresh object whose constructor kept <c>this</c>, which the ownership rules do not exclude.
/// </summary>
internal sealed class BodyHolders
{
    public static readonly BodyHolders None = new(UnseenWrites.None, new Dictionary<object, List<object>>(), new HashSet<LocalVariable>());

    private readonly Dictionary<object, List<object>> aliases;

    private BodyHolders(IReadOnlySet<object> unseen, Dictionary<object, List<object>> aliases, IReadOnlySet<LocalVariable> held)
    {
        Unseen = unseen;
        this.aliases = aliases;
        Held = held;
    }

    public IReadOnlySet<object> Unseen { get; }

    public IReadOnlySet<LocalVariable> Held { get; }

    public IReadOnlyList<object> AliasesOf(object holder)
    {
        return aliases.TryGetValue(holder, out var found) ? found : [];
    }

    public static bool HoldsMembers(LocalVariable local)
    {
        return local is { IsRef: false, Ownership: OwnershipKind.Owned };
    }

    public bool Names(LocalVariable local)
    {
        return !local.IsRef || (local.Parameter is null && aliases.TryGetValue(local, out var group)
            && group.All(other => other is not LocalVariable { IsRef: true, Parameter: not null }));
    }

    public static object? Named(Expression? expression)
    {
        return expression switch
        {
            LocalReferenceExpression local => local.Local,
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult field } => field.Field,
            _ => null,
        };
    }

    public static BodyHolders Of(object root, MemberWrites? writes = null)
    {
        writes ??= new MemberWrites(new FieldEffects());
        var nodes = BodyWalker.DescendantsAndSelf(root).ToList();
        var aliases = Aliases(nodes);
        var unseen = new HashSet<object>(UnseenWrites.Of(root), ReferenceEqualityComparer.Instance);
        foreach(var holder in unseen.ToList())
        {
            unseen.UnionWith(aliases.TryGetValue(holder, out var group) ? group : []);
        }

        return new BodyHolders(unseen, aliases, HeldReceivers(nodes, writes));
    }

    private static Dictionary<object, List<object>> Aliases(List<object> nodes)
    {
        var groups = new Dictionary<object, HashSet<object>>(ReferenceEqualityComparer.Instance);
        foreach(var declaration in nodes.OfType<LocalDeclarationStatement>())
        {
            if(declaration.Local.IsRef && Named(declaration.Initializer) is { } target)
            {
                var joined = Group(groups, declaration.Local);
                joined.UnionWith(Group(groups, target));
                foreach(var member in joined)
                {
                    groups[member] = joined;
                }
            }
        }

        return groups.ToDictionary(pair => pair.Key, pair => pair.Value.Where(other => !ReferenceEquals(other, pair.Key)).ToList(), ReferenceEqualityComparer.Instance);
    }

    private static HashSet<object> Group(Dictionary<object, HashSet<object>> groups, object holder)
    {
        return groups.TryGetValue(holder, out var group) ? group : new HashSet<object>(ReferenceEqualityComparer.Instance) { holder };
    }

    private static HashSet<LocalVariable> HeldReceivers(List<object> nodes, MemberWrites writes)
    {
        var seen = new HashSet<LocalReferenceExpression>(ReferenceEqualityComparer.Instance);
        var excluded = new HashSet<LocalVariable>(ReferenceEqualityComparer.Instance);

        void see(Expression? expression)
        {
            if(expression is LocalReferenceExpression reference)
            {
                _ = seen.Add(reference);
            }
        }

        void moved(Expression? expression)
        {
            if(expression is LocalReferenceExpression { Local.Ownership: OwnershipKind.Owned })
            {
                see(expression);
            }
        }

        void arguments(IReadOnlyList<Expression> values, IReadOnlyList<Parameter>? parameters)
        {
            for(var i = 0; parameters is not null && i < values.Count && i < parameters.Count; i++)
            {
                if(parameters[i].Modifier is { IsRef: true } or { Direction: not ByRefDirection.None }
                    or { Ownership: OwnershipKind.Owned or OwnershipKind.Borrowed or OwnershipKind.Frozen })
                {
                    see(values[i]);
                }
            }
        }

        void captured(IEnumerable<LocalVariable> parameters, IReadOnlyList<object> inside)
        {
            var own = Captures.OwnNames(parameters, inside);
            excluded.UnionWith(inside.OfType<LocalReferenceExpression>().Select(reference => reference.Local).Where(local => !own.Contains(local)));
        }

        foreach(var node in nodes)
        {
            switch(node)
            {
                case MemberAccessExpression { IsConditional: false, IsFailed: false, Member: FieldResult { Field.Modifiers.IsStatic: false } } access:
                    see(access.Receiver);
                    break;
                case InvocationExpression call:
                    if(writes.Of(call) is not null && Origins.IsUnchangeable(call.Type) && call.Method is not { Modifiers.IsRef: true })
                    {
                        see(ThisTiers.ReceiverOf(call));
                    }

                    arguments(call.Arguments, call.Parameters);
                    break;
                case NewExpression creation:
                    arguments(creation.Arguments, creation.Constructor?.ParametersList);
                    break;
                case AssignmentExpression assignment:
                    see(assignment.Target);
                    moved(assignment.Value);
                    break;
                case LocalDeclarationStatement { Local: { IsRef: false, Ownership: OwnershipKind.Unrestricted or OwnershipKind.Owned } } declaration:
                    moved(declaration.Initializer);
                    break;
                case LocalDeclarationStatement { Local.IsRef: true, Initializer: { } aliased } when Named(aliased) is null:
                    excluded.UnionWith(BodyWalker.DescendantsAndSelf(aliased).OfType<LocalReferenceExpression>().Select(reference => reference.Local));
                    break;
                case ReturnStatement { IsRef: false } returned:
                    moved(returned.Value);
                    break;
                case LambdaExpression lambda:
                    captured(lambda.Parameters, Captures.Inside(lambda));
                    break;
                case LocalFunctionStatement function:
                    captured(function.Parameters, Captures.Inside(function));
                    break;
                case AsyncBlockExpression block:
                    captured([], BodyWalker.DescendantsAndSelf(block.Body).ToList());
                    break;
            }
        }

        var references = nodes.OfType<LocalReferenceExpression>().Where(reference => HoldsMembers(reference.Local)).ToList();
        excluded.UnionWith(references.Where(reference => !seen.Contains(reference)).Select(reference => reference.Local));
        return new HashSet<LocalVariable>(references.Select(reference => reference.Local).Where(local => !excluded.Contains(local)), ReferenceEqualityComparer.Instance);
    }
}