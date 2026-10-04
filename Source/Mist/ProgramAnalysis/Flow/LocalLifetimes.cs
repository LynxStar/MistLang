using System.Collections.Immutable;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Holdings = System.Collections.Immutable.ImmutableDictionary<Mist.ProgramModel.Model.LocalVariable, Mist.ProgramAnalysis.Flow.Holding>;

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
    private static readonly Holdings Nothing = ImmutableDictionary.Create<LocalVariable, Holding>(ReferenceEqualityComparer.Instance);

    private readonly IReadOnlyDictionary<LocalVariable, (Block Home, LocalDeclarationStatement Declaration)> homes;
    private readonly HashSet<object> callees;

    private LocalLifetimes(IReadOnlyDictionary<LocalVariable, (Block Home, LocalDeclarationStatement Declaration)> homes, object root)
    {
        this.homes = homes;
        callees = BodyWalker.DescendantsAndSelf(root)
            .OfType<InvocationExpression>()
            .Select(invocation => invocation.Receiver)
            .Where(receiver => receiver is MemberAccessExpression { Member: null or MethodGroupResult })
            .ToHashSet<object>(ReferenceEqualityComparer.Instance);
    }

    public static bool IsTracked(IMistType type)
    {
        return PurityJudge.DefinitionOf(type) is ConcreteType { Discipline: SubstructuralDiscipline.Linear } and not StructType { IsRef: true }
            && !TaskResult.IsTask(type);
    }

    public static LifetimePlan Plan(object root)
    {
        var declared = Declared(root);

        if(declared.Count == 0)
        {
            return LifetimePlan.Empty;
        }

        var plan = new LifetimePlan(declared.ToDictionary(pair => pair.Key, pair => pair.Value.Declaration));
        var owners = declared
            .Where(pair => pair.Key is { IsRef: false, Ownership: not (OwnershipKind.Frozen or OwnershipKind.Borrowed) })
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if(owners.Count > 0)
        {
            new LocalLifetimes(owners, root).Solve(FlowGraphBuilder.Build(root), plan);
        }

        return plan;
    }

    public Holdings Join(Holdings left, Holdings right)
    {
        var joined = left;

        foreach(var (local, holding) in right)
        {
            joined = joined.SetItem(local, joined.TryGetValue(local, out var other) ? other | holding : holding);
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
        foreach(var (local, owned, _) in Changes(state, step))
        {
            state = state.SetItem(local, owned ? Holding.Owned : Holding.Unowned);
        }

        return step switch
        {
            { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration } when homes.ContainsKey(declaration.Local)
                => state.SetItem(declaration.Local, declaration.Initializer is null ? Holding.Unowned : Holding.Owned),
            { Kind: FlowStepKind.Leave, Node: Block block }
                => state.RemoveRange(state.Keys.Where(local => ReferenceEquals(homes[local].Home, block))),
            _ => state,
        };
    }

    private static Dictionary<LocalVariable, (Block Home, LocalDeclarationStatement Declaration)> Declared(object root)
    {
        var declared = new Dictionary<LocalVariable, (Block Home, LocalDeclarationStatement Declaration)>(ReferenceEqualityComparer.Instance);

        foreach(var block in BodyWalker.DescendantsAndSelf(root).OfType<Block>())
        {
            foreach(var declaration in block.Statements.OfType<LocalDeclarationStatement>())
            {
                if(IsTracked(declaration.Local.Type))
                {
                    declared[declaration.Local] = (block, declaration);
                }
            }
        }

        return declared;
    }

    private void Solve(FlowGraph graph, LifetimePlan plan)
    {
        FlowSolution<Holdings>.Solve(graph, this, Nothing).Replay((state, step) => Record(state, step, plan));

        foreach(var nested in graph.Nested.Values)
        {
            Solve(nested, plan);
        }
    }

    private void Record(Holdings state, FlowStep step, LifetimePlan plan)
    {
        foreach(var (local, owned, at) in Changes(state, step))
        {
            plan.Mark(at, local, owned);
        }

        if(step is { Kind: FlowStepKind.Leave, Node: Block block })
        {
            foreach(var (local, holding) in state)
            {
                if(ReferenceEquals(homes[local].Home, block) && holding.HasFlag(Holding.Owned))
                {
                    plan.Owe(local);
                }
            }
        }

        if(MayThrow(step))
        {
            foreach(var (local, holding) in Transfer(state, step))
            {
                if(holding.HasFlag(Holding.Owned))
                {
                    plan.Owe(local);
                }
            }
        }
    }

    private IEnumerable<(LocalVariable Local, bool Owned, Expression At)> Changes(Holdings state, FlowStep step)
    {
        switch(step)
        {
            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: { } initializer } declaration }
                when homes.ContainsKey(declaration.Local) && Source(initializer, state) is { } source:
                yield return (source, false, initializer);
                break;

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false, Target: LocalReferenceExpression { Local: var target } } assignment }
                when state.ContainsKey(target):
                if(Source(assignment.Value, state) is { } moved && !ReferenceEquals(moved, target))
                {
                    yield return (moved, false, assignment.Value);
                }

                yield return (target, true, assignment);
                break;

            case { Kind: FlowStepKind.Evaluate, Node: ReturnStatement { Value: { } returned } } when Source(returned, state) is { } source:
                yield return (source, false, returned);
                break;

            case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression invocation }:
                foreach(var passed in Passed(invocation.Arguments, invocation.Parameters, state, lending: true))
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
                foreach(var passed in Passed(creation.Arguments, creation.Constructor?.ParametersList, state, lending: false))
                {
                    yield return passed;
                }

                break;
        }
    }

    private static IEnumerable<(LocalVariable, bool, Expression)> Passed(
        IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, Holdings state, bool lending)
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(Source(arguments[i], state) is { } source && !(lending && ByReference(parameters, i)))
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

    private static LocalVariable? Source(Expression expression, Holdings state)
    {
        return expression is LocalReferenceExpression { Local: var local } && state.ContainsKey(local) ? local : null;
    }

    private bool MayThrow(FlowStep step)
    {
        return step.Kind == FlowStepKind.Evaluate && !callees.Contains(step.Node) && step.Node switch
        {
            AssignmentExpression { Target: LocalReferenceExpression } => false,
            Expression expression => MayThrow(expression),
            ReturnStatement or BreakStatement or LocalFunctionStatement => false,
            _ => true,
        };
    }

    private static bool MayThrow(Expression expression)
    {
        return expression is not (LiteralExpression or LocalReferenceExpression or ThisExpression or DefaultExpression
            or LambdaExpression or FunctionReferenceExpression or MethodGroupExpression or MethodReferenceExpression
            or TypeReferenceExpression or TypeLiteralExpression or NamespaceReferenceExpression or VoidLiteralExpression);
    }

    private static bool HasNaturalType(Expression expression)
    {
        return expression is InvocationExpression or NewExpression or MemberAccessExpression or FieldReferenceExpression
            or PropertyReferenceExpression or IndexExpression or ElementReadExpression or BinaryExpression or UnaryExpression
            or CastExpression or InterpolatedStringExpression or CloneExpression;
    }
}