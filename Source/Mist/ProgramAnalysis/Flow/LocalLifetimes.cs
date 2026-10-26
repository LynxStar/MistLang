using System.Collections.Immutable;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Holdings = System.Collections.Immutable.ImmutableDictionary<object, Mist.ProgramAnalysis.Flow.Holding>;

namespace Mist.ProgramAnalysis.Flow;

[Flags]
internal enum Holding
{
    Owned = 1,
    Unowned = 2,
}

/// <summary>
/// A callee owns what it is given from the moment it is entered and releases it itself if it throws,
/// so the hand-offs of a call are made before its own throw is counted. A task keeps the subtree
/// approximation its promotion reads.
/// </summary>
internal sealed class LocalLifetimes : IFlowLattice<Holdings>
{
    private static readonly Holdings Nothing = ImmutableDictionary.Create<object, Holding>(ReferenceEqualityComparer.Instance);

    private readonly LifetimeOwners owners;
    private readonly HashSet<object> callees;
    private readonly Dictionary<object, List<WithBlockStatement>> enclosing = new(ReferenceEqualityComparer.Instance);

    private LocalLifetimes(LifetimeOwners owners, object root)
    {
        this.owners = owners;
        callees = BodyWalker.DescendantsAndSelf(root)
            .OfType<InvocationExpression>()
            .Select(invocation => invocation.Receiver)
            .Where(receiver => receiver is MemberAccessExpression { Member: null or MethodGroupResult })
            .ToHashSet<object>(ReferenceEqualityComparer.Instance);

        foreach(var (scope, _) in owners.Scopes)
        {
            Enclose(scope);
        }
    }

    public static bool IsTracked(IMistType type)
    {
        return PurityJudge.DefinitionOf(type) is ConcreteType { Discipline: SubstructuralDiscipline.Linear }
            && !TaskResult.IsTask(type)
            && PurityJudge.Drops(type, awaitable: false).Any();
    }

    public static bool IsScoped(IMistType type)
    {
        return PurityJudge.DefinitionOf(type) is ConcreteType { Discipline: SubstructuralDiscipline.Affine }
            && PurityJudge.Drops(type, awaitable: false).Any();
    }

    public static LifetimePlan Plan(object root, Method? method = null, ConcreteType? holder = null)
    {
        var owners = LifetimeOwners.Of(root, method, holder);

        if(owners.Count == 0 && owners.Declarations.Count == 0)
        {
            return LifetimePlan.Empty;
        }

        var plan = new LifetimePlan(owners.Declarations);

        if(owners.Count > 0)
        {
            var lifetimes = new LocalLifetimes(owners, root);
            var entry = owners.Entered.Aggregate(Nothing, (state, owner) => state.SetItem(owner, Holding.Owned));
            lifetimes.Solve(FlowGraphBuilder.Build(root), plan, entry);
            plan.Settle();
        }

        return plan;
    }

    public Holdings Join(Holdings left, Holdings right)
    {
        var joined = left;

        foreach(var (owner, holding) in right)
        {
            joined = joined.SetItem(owner, joined.TryGetValue(owner, out var other) ? other | holding : holding);
        }

        return joined;
    }

    public bool Same(Holdings left, Holdings right)
    {
        return left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var holding) && holding == pair.Value);
    }

    public Holdings Transfer(Holdings state, FlowStep step)
    {
        foreach(var (owner, owned, _) in Changes(state, step))
        {
            state = state.SetItem(owner, owned ? Holding.Owned : Holding.Unowned);
        }

        switch(step)
        {
            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration } when owners.Owns(declaration.Local):
                return state.SetItem(declaration.Local, declaration.Initializer is null ? Holding.Unowned : Holding.Owned);

            case { Kind: FlowStepKind.Introduce, Variable: { } variable } when owners.Introduced(variable) is { } introduced:
                return state.SetItem(introduced, Holding.Owned);

            case { Kind: FlowStepKind.Leave, Node: var scope }:
                if(owners.ScopeEndingAt(scope) is { Owner: var released } && state.ContainsKey(released))
                {
                    state = state.SetItem(released, Holding.Unowned);
                }

                return state.RemoveRange(state.Keys.Where(owner => owners.IsHome(owner, scope)));

            default:
                return state;
        }
    }

    private void Solve(FlowGraph graph, LifetimePlan plan, Holdings entry)
    {
        foreach(var owner in owners.All)
        {
            plan.Cover(owner, LifetimeOwners.AutomaticallyDrops(owner));
        }

        Settle(graph, plan, entry);
    }

    private static InvocationExpression? CallOf(FlowStep step)
    {
        return step.Kind == FlowStepKind.Evaluate ? step.Node as InvocationExpression : null;
    }

    private void Settle(FlowGraph graph, LifetimePlan plan, Holdings entry)
    {
        FlowSolution<Holdings>.Solve(graph, this, entry).Replay((state, step) => Record(state, step, plan));

        foreach(var nested in graph.Nested.Values)
        {
            Settle(nested, plan, Nothing);
        }
    }

    private void Record(Holdings state, FlowStep step, LifetimePlan plan)
    {
        var call = CallOf(step);
        var entry = call is null ? CallEntry.Certain : CallEntries.Of(call, receiver => owners.OwnerOf(receiver) is not null);

        foreach(var (owner, owned, at) in Changes(state, step))
        {
            var passed = !owned && entry is not CallEntry.Certain && !(call?.Receiver is MemberAccessExpression { Receiver: var receiver } && ReferenceEquals(receiver, at));
            plan.Mark(at, owner, owned, passed && entry is CallEntry.Absentable ? call : null);

            if(passed && state.TryGetValue(owner, out var holding) && holding.HasFlag(Holding.Owned))
            {
                plan.Owe(owner);
            }
        }

        if(step.Kind == FlowStepKind.Leave)
        {
            if(owners.ScopeEndingAt(step.Node) is var (scope, released) && state.TryGetValue(released, out var held))
            {
                plan.Scope(scope, released, held);
            }

            foreach(var (owner, holding) in state)
            {
                if(owners.IsHome(owner, step.Node) && holding.HasFlag(Holding.Owned))
                {
                    plan.Owe(owner);
                }
            }
        }

        if(MayThrow(step))
        {
            var after = Transfer(state, step);

            foreach(var (owner, holding) in after)
            {
                if(holding.HasFlag(Holding.Owned))
                {
                    plan.Owe(owner);
                }
            }

            foreach(var scope in Enclosing(step.Node))
            {
                if(owners.ScopeEndingAt(scope.Body) is { Owner: var released } && after.TryGetValue(released, out var held))
                {
                    plan.Scope(scope, released, held);
                }
            }
        }
    }

    /// <summary>
    /// The with scopes a throw from this node leaves through, each of whose finally releases what its
    /// holder still owns there -- so a throw is one more way out of the scope, judged like its exits. A
    /// lambda's body is not inside: it throws when it is called, not where it is written.
    /// </summary>
    private void Enclose(WithBlockStatement scope)
    {
        var pending = new Stack<object>();
        pending.Push(scope.Body);

        while(pending.Count > 0)
        {
            var node = pending.Pop();

            if(!enclosing.TryGetValue(node, out var scopes))
            {
                scopes = [];
                enclosing.Add(node, scopes);
            }

            scopes.Add(scope);

            if(node is LambdaExpression or LocalFunctionStatement or AsyncBlockExpression)
            {
                continue;
            }

            foreach(var child in BodyWalker.ChildrenOf(node))
            {
                pending.Push(child);
            }
        }
    }

    private List<WithBlockStatement> Enclosing(object node)
    {
        return enclosing.GetValueOrDefault(node) ?? [];
    }

    private IEnumerable<(object Owner, bool Owned, Expression At)> Changes(Holdings state, FlowStep step)
    {
        switch(step)
        {
            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: { } initializer } declaration }
                when owners.Owns(declaration.Local) && Source(initializer, state) is { } source:
                yield return (source, false, initializer);
                break;

            case { Kind: FlowStepKind.Introduce, Variable: { } introduced }
                when owners.ElementOf(introduced) is { } element && Source(element, state) is { } source:
                yield return (source, false, element);
                break;

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false } assignment }:
                if(owners.TargetOf(assignment.Target) is { } target && state.ContainsKey(target))
                {
                    if(Source(assignment.Value, state) is { } moved && !ReferenceEquals(moved, target))
                    {
                        yield return (moved, false, assignment.Value);
                    }

                    yield return (target, true, assignment);
                }
                else if(Keeps(assignment.Target) && Source(assignment.Value, state) is { } stored)
                {
                    yield return (stored, false, assignment.Value);
                }

                break;

            case { Kind: FlowStepKind.Evaluate, Node: ReturnStatement { Value: { } returned } } when Source(returned, state) is { } source:
                yield return (source, false, returned);
                break;

            case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression invocation }:
                foreach(var passed in Passed(invocation.Arguments, invocation.Parameters, state))
                {
                    yield return passed;
                }

                if(invocation is { Method.Name: "Drop", Receiver: MemberAccessExpression { Receiver: var receiver } }
                    && Source(receiver, state) is { } dropped)
                {
                    yield return (dropped, false, receiver);
                }

                break;

            case { Kind: FlowStepKind.Evaluate, Node: NewExpression creation }:
                foreach(var passed in Passed(creation.Arguments, creation.Constructor?.ParametersList, state))
                {
                    yield return passed;
                }

                foreach(var entry in creation.Initializer)
                {
                    if(entry.Member is FieldResult { Field: var field } && Keeps(field) && Source(entry.Value, state) is { } stored)
                    {
                        yield return (stored, false, entry.Value);
                    }
                }

                break;
        }
    }

    private static bool Keeps(Expression target)
    {
        return ThisTiers.OwnedSlot(target) is not null || target switch
        {
            FieldReferenceExpression { Field: var field } => Keeps(field),
            MemberAccessExpression { Member: FieldResult { Field: var field } } => Keeps(field),
            _ => false,
        };
    }

    private static bool Keeps(Field field)
    {
        return !field.Modifiers.IsStatic && (ThisTiers.IsOwned(field) || IsTracked(field.Type));
    }

    private IEnumerable<(object, bool, Expression)> Passed(
        IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, Holdings state)
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(Source(arguments[i], state) is { } source && !Lends(parameters, i)
                && (!IsScoped(arguments[i].Type)
                    || (parameters is not null && i < parameters.Count && parameters[i].Modifier.Ownership == OwnershipKind.Owned)))
            {
                yield return (source, false, Settled(arguments, i, parameters));
            }
        }
    }

    private static Expression Settled(IReadOnlyList<Expression> arguments, int handed, IReadOnlyList<Parameter>? parameters)
    {
        for(var last = arguments.Count - 1; last > handed; last--)
        {
            if(MayThrow(arguments[last]))
            {
                return HasNaturalType(arguments[last]) && !ByReference(parameters, last) ? arguments[last] : arguments[handed];
            }
        }

        return arguments[handed];
    }

    private static bool ByReference(IReadOnlyList<Parameter>? parameters, int index)
    {
        return parameters is not null && index < parameters.Count && parameters[index].Modifier.IsRef;
    }

    private static bool Lends(IReadOnlyList<Parameter>? parameters, int index)
    {
        return ByReference(parameters, index)
            || (parameters is not null && index < parameters.Count && parameters[index].Modifier.Ownership is OwnershipKind.Frozen or OwnershipKind.Borrowed);
    }

    private object? Source(Expression expression, Holdings state)
    {
        return owners.OwnerOf(expression) is { } owner && state.ContainsKey(owner) ? owner : null;
    }

    private bool MayThrow(FlowStep step)
    {
        return step.Kind == FlowStepKind.Evaluate && !callees.Contains(step.Node) && step.Node switch
        {
            AssignmentExpression { Target: LocalReferenceExpression } => false,
            Expression expression => MayThrow(expression),
            ReturnStatement or BreakStatement or LocalFunctionStatement or ObjectInitializerAssignment => false,
            _ => true,
        };
    }

    private static bool MayThrow(Expression expression)
    {
        return expression is not (LiteralExpression or LocalReferenceExpression or ThisExpression or DefaultExpression
            or LambdaExpression or FunctionReferenceExpression or MethodGroupExpression or MethodReferenceExpression
            or TypeReferenceExpression or TypeLiteralExpression or NamespaceReferenceExpression or VoidLiteralExpression or TupleLiteralExpression
            or FieldReferenceExpression { Field.Modifiers.IsStatic: false }
            or MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult { Field.Modifiers.IsStatic: false } });
    }

    private static bool HasNaturalType(Expression expression)
    {
        return expression is InvocationExpression or NewExpression or MemberAccessExpression or FieldReferenceExpression
            or PropertyReferenceExpression or IndexExpression or ElementReadExpression or BinaryExpression or UnaryExpression
            or CastExpression or InterpolatedStringExpression or CloneExpression or ArrayLiteralExpression or ArrayCreationExpression;
    }
}