using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// A ref local names its referent's storage, so a write through it ends every holder it may name (flow-sensitive
/// refinement inference Requirement 3.6), and those keep their facts between such writes. A ref returned by a call
/// may name anything the call reaches as well as what it was handed by ref, so a ref local bound to one carries no
/// facts itself, while a write through it ends those holders. Only the body's own locals and by-value parameters are
/// followed: a field or a by-ref parameter may share storage with a by-ref parameter whose writes end nothing else,
/// and a local a nested function reads may be the storage of that function's own by-ref parameter, so any of these
/// handed out by reference keeps only its type.
/// </summary>
internal sealed class BodyHolders
{
    public static readonly BodyHolders None = new(UnseenWrites.None, new Dictionary<object, List<object>>(), new Dictionary<object, List<object>>());

    private readonly Dictionary<object, List<object>> aliases;
    private readonly Dictionary<object, List<object>> reached;

    private BodyHolders(IReadOnlySet<object> unseen, Dictionary<object, List<object>> aliases, Dictionary<object, List<object>> reached)
    {
        Unseen = unseen;
        this.aliases = aliases;
        this.reached = reached;
    }

    public IReadOnlySet<object> Unseen { get; }

    public IReadOnlyList<object> AliasesOf(object holder)
    {
        var group = aliases.TryGetValue(holder, out var found) ? found : [];
        return group.Prepend(holder).SelectMany(member => reached.TryGetValue(member, out var named) ? named : []).Concat(group)
            .Where(other => !ReferenceEquals(other, holder)).Distinct(ReferenceEqualityComparer.Instance).ToList();
    }

    public static (IReadOnlyList<Expression>? Arguments, IReadOnlyList<Parameter>? Parameters) Arguments(Expression expression)
    {
        return expression switch
        {
            InvocationExpression call => (call.Arguments, call.Parameters),
            NewExpression creation => (creation.Arguments, creation.Constructor?.ParametersList),
            _ => (null, null),
        };
    }

    public static IEnumerable<Expression> Places(Expression expression)
    {
        var (arguments, parameters) = Arguments(expression);
        for(var i = 0; arguments is not null && parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.Direction == ByRefDirection.None)
            {
                continue;
            }

            foreach(var place in ReturnsRef(arguments[i]) ? Places(arguments[i]) : [arguments[i]])
            {
                yield return place;
            }
        }

        if(expression is InvocationExpression invocation && ThisTiers.ReceiverOf(invocation) is { Type: StructType } receiver)
        {
            yield return receiver;
        }
    }

    public static bool ReturnsRef(Expression expression)
    {
        return expression is InvocationExpression { Method.Modifiers.IsRef: true };
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

    public static BodyHolders Of(object root)
    {
        var nodes = BodyWalker.DescendantsAndSelf(root).ToList();
        var unseen = new HashSet<object>(UnseenWrites.Of(root), ReferenceEqualityComparer.Instance);
        var aliases = Aliases(nodes);
        var reached = Reached(nodes, aliases);
        var captured = Captured(nodes);
        var handed = nodes.OfType<Expression>().SelectMany(Handed).Select(Named).OfType<object>()
            .Concat(aliases.Keys).Concat(reached.Values.SelectMany(named => named));
        unseen.UnionWith(handed.Where(holder => !Followed(holder) || captured.Contains(holder)));
        var pending = new Stack<object>(unseen);
        while(pending.TryPop(out var holder))
        {
            var group = aliases.TryGetValue(holder, out var members) ? members : [];
            foreach(var other in group.Concat(reached.TryGetValue(holder, out var named) ? named : []))
            {
                if(unseen.Add(other))
                {
                    pending.Push(other);
                }
            }
        }

        foreach(var opaque in reached.Keys.ToList())
        {
            _ = unseen.Add(opaque);
            unseen.UnionWith(aliases.TryGetValue(opaque, out var group) ? group : []);
        }

        return new BodyHolders(unseen, aliases, reached);
    }

    private static bool Followed(object holder)
    {
        return holder is LocalVariable { Parameter: null } or LocalVariable { Parameter.Modifier: { IsRef: false, Direction: ByRefDirection.None } };
    }

    private static IEnumerable<Expression> Handed(Expression expression)
    {
        var (arguments, parameters) = Arguments(expression);
        for(var i = 0; arguments is not null && parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.Direction != ByRefDirection.None)
            {
                foreach(var place in ReturnsRef(arguments[i]) ? Places(arguments[i]) : [arguments[i]])
                {
                    yield return place;
                }
            }
        }
    }

    private static HashSet<LocalVariable> Captured(List<object> nodes)
    {
        var captured = new HashSet<LocalVariable>(ReferenceEqualityComparer.Instance);
        void inside(IEnumerable<LocalVariable> parameters, IReadOnlyList<object> body)
        {
            var own = Captures.OwnNames(parameters, body);
            captured.UnionWith(body.OfType<LocalReferenceExpression>().Select(reference => reference.Local).Where(local => !own.Contains(local)));
        }

        foreach(var node in nodes)
        {
            switch(node)
            {
                case LambdaExpression lambda:
                    inside(lambda.Parameters, Captures.Inside(lambda));
                    break;
                case LocalFunctionStatement function:
                    inside(function.Parameters, Captures.Inside(function));
                    break;
                case AsyncBlockExpression block:
                    inside([], BodyWalker.DescendantsAndSelf(block.Body).ToList());
                    break;
            }
        }

        return captured;
    }

    private static Dictionary<object, List<object>> Reached(List<object> nodes, Dictionary<object, List<object>> aliases)
    {
        var reached = new Dictionary<object, List<object>>(ReferenceEqualityComparer.Instance);
        foreach(var declaration in nodes.OfType<LocalDeclarationStatement>())
        {
            if(declaration.Local.IsRef && ReturnsRef(declaration.Initializer!))
            {
                var found = new HashSet<object>(ReferenceEqualityComparer.Instance);
                foreach(var holder in Places(declaration.Initializer!).Select(Named).OfType<object>())
                {
                    var group = aliases.TryGetValue(holder, out var members) ? members : [];
                    found.UnionWith(group.Prepend(holder));
                    found.UnionWith(group.Prepend(holder).SelectMany(member => reached.TryGetValue(member, out var named) ? named : []));
                }

                _ = found.Remove(declaration.Local);
                reached[declaration.Local] = [.. found];
            }
        }

        return reached;
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
}