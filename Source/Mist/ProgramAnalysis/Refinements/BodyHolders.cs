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
/// passed by ref or out, or named by a ref local, keeps only its type; a readonly ref argument cannot write it.
/// </summary>
internal sealed class BodyHolders
{
    public static readonly BodyHolders None = new(UnseenWrites.None, new Dictionary<object, List<object>>(), new Dictionary<object, List<object>>(),
        new Dictionary<object, WriteReach>(), new HashSet<object>(), new HashSet<object>());

    private readonly Dictionary<object, List<object>> aliases;
    private readonly Dictionary<object, List<object>> reached;
    private readonly Dictionary<object, WriteReach> storage;
    private readonly IReadOnlySet<object> created;
    private readonly IReadOnlySet<object> owned;

    private BodyHolders(IReadOnlySet<object> unseen, Dictionary<object, List<object>> aliases, Dictionary<object, List<object>> reached,
        Dictionary<object, WriteReach> storage, IReadOnlySet<object> created, IReadOnlySet<object> owned)
    {
        Unseen = unseen;
        this.aliases = aliases;
        this.reached = reached;
        this.storage = storage;
        this.created = created;
        this.owned = owned;
    }

    public IReadOnlySet<object> Unseen { get; }

    public IReadOnlyList<object> AliasesOf(object holder)
    {
        var group = aliases.TryGetValue(holder, out var found) ? found : [];
        return group.Prepend(holder).SelectMany(member => reached.TryGetValue(member, out var named) ? named : []).Concat(group)
            .Where(other => !ReferenceEquals(other, holder)).Distinct(ReferenceEqualityComparer.Instance).ToList();
    }

    public WriteReach Reach(Expression target)
    {
        return target switch
        {
            LocalReferenceExpression { Local: var local } => AliasesOf(local).Prepend(local).Aggregate(WriteReach.Nothing, (reach, holder) => reach.Or(holder switch
            {
                Field field => WriteReach.Field(field),
                LocalVariable { Parameter.Modifier.IsRef: true } parameter => WriteReach.Storage(parameter.Type, anyField: true),
                _ => storage.GetValueOrDefault(holder) ?? WriteReach.Nothing,
            })),
            FieldReferenceExpression { Field: var field } => WriteReach.Field(field),
            MemberAccessExpression { Member: FieldResult { Field: var field }, Receiver: var receiver } => Member(field, receiver),
            IndexExpression { Indexer: null } element when Owned(element.Receiver, owned) => WriteReach.Nothing,
            IndexExpression element => WriteReach.Storage(element.Type, anyField: element.Indexer?.Modifiers.IsRef == true),
            InvocationExpression call when ReturnsRef(call) => ReturnedStorage.Of(call, this),
            _ => WriteReach.Nothing,
        };
    }

    public WriteReach Member(Field field, Expression receiver)
    {
        return !field.Modifiers.IsRef && Owned(receiver, owned) ? WriteReach.Storage(field.Type) with { ByRefParameters = false }
            : Distinct(receiver, created) ? WriteReach.Storage(field.Type) : WriteReach.Field(field);
    }

    private static bool Owned(Expression storage, IReadOnlySet<object> owned)
    {
        return storage switch
        {
            NewExpression or ArrayCreationExpression => true,
            LocalReferenceExpression { Local: var local } => owned.Contains(local),
            MemberAccessExpression { Member: FieldResult { Field: { Modifiers.IsRef: false } field }, Receiver: var receiver } =>
                Requirement.Unrefined(field.Type) is StructType && Owned(receiver, owned),
            _ => false,
        };
    }

    private static bool Distinct(Expression receiver, IReadOnlySet<object> created)
    {
        return receiver switch
        {
            NewExpression => true,
            LocalReferenceExpression { Local: var local } when created.Contains(local) => true,
            LocalReferenceExpression { Local: { IsRef: false, Parameter: null or { Modifier.IsRef: false } } local } => local.Type is StructType,
            _ => false,
        };
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

        var created = Created(nodes, unseen, value => value is NewExpression);
        var owned = Created(nodes, unseen, value => value is ArrayCreationExpression).Concat(created)
            .Concat(nodes.OfType<LocalDeclarationStatement>().Select(declaration => declaration.Local)
                .Where(local => local is { IsRef: false, Parameter: null } && Requirement.Unrefined(local.Type) is StructType))
            .Concat(nodes.OfType<LocalReferenceExpression>().Select(reference => reference.Local)
                .Where(local => local is { IsRef: false, Parameter.Modifier: { IsRef: false, Direction: ByRefDirection.None } } && Requirement.Unrefined(local.Type) is StructType))
            .Where(local => !captured.Contains((LocalVariable)local)).ToHashSet(ReferenceEqualityComparer.Instance);
        var holders = new BodyHolders(unseen, aliases, reached, new Dictionary<object, WriteReach>(ReferenceEqualityComparer.Instance), created, owned);
        holders.Store(nodes);
        return holders;
    }

    private void Store(List<object> nodes)
    {
        foreach(var declaration in nodes.OfType<LocalDeclarationStatement>())
        {
            if(declaration is { Local.IsRef: true, Initializer: { } initializer } && Named(initializer) is null)
            {
                storage[declaration.Local] = initializer switch
                {
                    _ when ReturnsRef(initializer) => ReturnedStorage.Of(initializer, this),
                    MemberAccessExpression { Member: FieldResult { Field: { Modifiers.IsRef: false } field }, Receiver: var receiver } when Owned(receiver, owned) =>
                        WriteReach.Field(field) with { ByRefParameters = false },
                    MemberAccessExpression { Member: FieldResult { Field: var field } } => WriteReach.Field(field),
                    IndexExpression { Indexer: null } element when Owned(element.Receiver, owned) => WriteReach.Nothing,
                    _ => WriteReach.Storage(declaration.Local.Type, anyField: initializer is not IndexExpression { Indexer: null }),
                };
            }
        }
    }

    private static HashSet<object> Created(List<object> nodes, HashSet<object> unseen, Func<Expression?, bool> creates)
    {
        var created = nodes.OfType<LocalDeclarationStatement>().Where(declaration => !declaration.Local.IsRef && creates(declaration.Initializer))
            .Select(declaration => (object)declaration.Local).ToHashSet(ReferenceEqualityComparer.Instance);
        created.ExceptWith(unseen);
        created.ExceptWith(nodes.OfType<Expression>().SelectMany(Handed).Select(Named).OfType<object>());
        created.ExceptWith(nodes.OfType<AssignmentExpression>().Where(assignment => !creates(assignment.Value)).Select(assignment => Named(assignment.Target)).OfType<object>());
        created.ExceptWith(nodes.OfType<LocalDeclarationStatement>().Where(declaration => declaration.Local.IsRef).Select(declaration => Named(declaration.Initializer)).OfType<object>());
        return created;
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
            if(parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out)
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