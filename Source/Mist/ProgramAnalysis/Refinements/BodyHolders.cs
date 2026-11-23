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
/// facts itself, while a write through it ends those holders. A local a nested function reads may be the storage of
/// that function's own by-ref parameter once it is handed by reference, so a write to either, or a call to a local
/// function that writes the local, ends the other; <see cref="WriteReach"/> names what every other write reaches.
/// </summary>
internal sealed class BodyHolders
{
    public static readonly BodyHolders None = new(UnseenWrites.None, new Dictionary<object, List<object>>(), new Dictionary<object, List<object>>(),
        new Dictionary<object, WriteReach>(), new HashSet<object>(), new HashSet<object>(), new Dictionary<object, List<LocalVariable>>(),
        new Dictionary<Method, WriteReach>());

    private readonly Dictionary<object, List<object>> aliases;
    private readonly Dictionary<object, List<object>> reached;
    private readonly Dictionary<object, WriteReach> storage;
    private readonly IReadOnlySet<object> created;
    private readonly IReadOnlySet<object> owned;
    private readonly Dictionary<object, List<LocalVariable>> shared;
    private readonly Dictionary<Method, WriteReach> calls;

    private BodyHolders(IReadOnlySet<object> unseen, Dictionary<object, List<object>> aliases, Dictionary<object, List<object>> reached,
        Dictionary<object, WriteReach> storage, IReadOnlySet<object> created, IReadOnlySet<object> owned, Dictionary<object, List<LocalVariable>> shared,
        Dictionary<Method, WriteReach> calls)
    {
        Unseen = unseen;
        this.aliases = aliases;
        this.reached = reached;
        this.storage = storage;
        this.created = created;
        this.owned = owned;
        this.shared = shared;
        this.calls = calls;
    }

    public IReadOnlySet<object> Unseen { get; }

    public WriteReach Called(Expression expression)
    {
        return expression is InvocationExpression { Method: { } method } && calls.TryGetValue(method.SourceDeclaration ?? method, out var reach) ? reach : WriteReach.Nothing;
    }

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
            }).Or(WriteReach.Shared(shared.GetValueOrDefault(holder) ?? []))),
            FieldReferenceExpression { Field: var field } => WriteReach.Field(field),
            MemberAccessExpression { Member: FieldResult { Field: var field }, Receiver: var receiver } => Member(field, receiver),
            IndexExpression { Indexer: null } element when Owned(element.Receiver, owned) => WriteReach.Nothing,
            IndexExpression element => WriteReach.Storage(element.Type, anyField: element.Indexer?.Modifiers.IsRef == true),
            InvocationExpression call when ReturnsRef(call) => ReturnedStorage.Of(call, this),
            _ => WriteReach.Nothing,
        };
    }

    public WriteReach Member(Field field, Expression receiver, bool nested = false)
    {
        return !field.Modifiers.IsRef && Owned(receiver, owned) ? WriteReach.Storage(field.Type) with { ByRefParameters = false }
            : !nested && Distinct(receiver, created) ? WriteReach.Storage(field.Type) : WriteReach.Field(field);
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
        var functions = Functions(nodes);
        var captured = new HashSet<LocalVariable>(functions.SelectMany(function => function.Reads), ReferenceEqualityComparer.Instance);
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
        var shared = Shared(nodes, functions, captured, aliases, reached);
        var holders = new BodyHolders(unseen, aliases, reached, new Dictionary<object, WriteReach>(ReferenceEqualityComparer.Instance), created, owned, shared,
            Calls(functions, shared));
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

    private static Dictionary<object, List<LocalVariable>> Shared(List<object> nodes, List<Nested> functions, HashSet<LocalVariable> captured, Dictionary<object, List<object>> aliases, Dictionary<object, List<object>> reached)
    {
        HashSet<object> lent(bool writable)
        {
            var named = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach(var holder in nodes.OfType<Expression>().SelectMany(expression => Passed(expression, writable)).Select(Named).OfType<object>())
            {
                named.UnionWith((aliases.TryGetValue(holder, out var group) ? group : []).Concat(reached.TryGetValue(holder, out var found) ? found : []).Prepend(holder));
            }

            return named;
        }

        var (written, read) = (lent(writable: true), lent(writable: false));
        var shared = new Dictionary<object, List<LocalVariable>>(ReferenceEqualityComparer.Instance);
        foreach(var function in functions)
        {
            foreach(var parameter in function.Parameters.Where(parameter => parameter.Parameter?.Modifier is { IsRef: true } or { Direction: not ByRefDirection.None }))
            {
                var pool = parameter.Parameter!.Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out ? written : read;
                foreach(var local in captured.Where(local => !function.Own.Contains(local) && pool.Contains(local) && WriteReach.MayShare(local.Type, parameter.Type)))
                {
                    (shared.TryGetValue(parameter, out var mine) ? mine : shared[parameter] = []).Add(local);
                    (shared.TryGetValue(local, out var theirs) ? theirs : shared[local] = []).Add(parameter);
                }
            }
        }

        return shared;
    }

    private static Dictionary<Method, WriteReach> Calls(List<Nested> functions, Dictionary<object, List<LocalVariable>> shared)
    {
        var writes = new Dictionary<Method, HashSet<object>>(ReferenceEqualityComparer.Instance);
        foreach(var function in functions.Where(function => function.Function is not null))
        {
            writes[function.Function!] = function.Writes;
        }

        for(var grown = true; grown;)
        {
            grown = false;
            foreach(var function in functions.Where(function => function.Function is not null))
            {
                var mine = writes[function.Function!];
                var before = mine.Count;
                mine.UnionWith(function.Calls.SelectMany(callee => writes.TryGetValue(callee, out var theirs) ? theirs : []).ToList());
                grown |= mine.Count != before;
            }
        }

        var calls = new Dictionary<Method, WriteReach>(ReferenceEqualityComparer.Instance);
        foreach(var (function, written) in writes)
        {
            calls[function] = WriteReach.Shared(written.SelectMany(local => shared.GetValueOrDefault(local) ?? []));
        }

        return calls;
    }

    public static IEnumerable<Expression> Handed(Expression expression)
    {
        return Passed(expression, writable: true);
    }

    private static IEnumerable<Expression> Passed(Expression expression, bool writable)
    {
        var (arguments, parameters) = Arguments(expression);
        for(var i = 0; arguments is not null && parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out || (!writable && parameters[i].Modifier.Direction is ByRefDirection.In))
            {
                foreach(var place in ReturnsRef(arguments[i]) ? Places(arguments[i]) : [arguments[i]])
                {
                    yield return place;
                }
            }
        }
    }

    private static List<Nested> Functions(List<object> nodes)
    {
        var functions = new List<Nested>();
        void inside(IReadOnlyList<LocalVariable> parameters, IReadOnlyList<object> body, Method? function)
        {
            var own = Captures.OwnNames(parameters, body);
            var reads = new HashSet<LocalVariable>(body.OfType<LocalReferenceExpression>().Select(reference => reference.Local).Where(local => !own.Contains(local)),
                ReferenceEqualityComparer.Instance);
            var calls = body.OfType<InvocationExpression>().Select(call => call.Method?.SourceDeclaration ?? call.Method).OfType<Method>().ToList();
            functions.Add(new(parameters, own, reads, new HashSet<object>(UnseenWrites.CapturedWrites(own, body), ReferenceEqualityComparer.Instance), function, calls));
        }

        foreach(var node in nodes)
        {
            switch(node)
            {
                case LambdaExpression lambda:
                    inside(lambda.Parameters, Captures.Inside(lambda), null);
                    break;
                case LocalFunctionStatement function:
                    inside(function.Parameters, Captures.Inside(function), function.Function);
                    break;
                case AsyncBlockExpression block:
                    inside([], BodyWalker.DescendantsAndSelf(block.Body).ToList(), null);
                    break;
            }
        }

        return functions;
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

    private sealed record Nested(IReadOnlyList<LocalVariable> Parameters, HashSet<LocalVariable> Own, HashSet<LocalVariable> Reads, HashSet<object> Writes,
        Method? Function, List<Method> Calls);
}