using System.Collections.Immutable;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Moves and leases on the flow framework, over <see cref="Place"/>s. A place is moved by a declaration
/// or assignment that takes it directly, by an owned parameter, an owned field or an owned constructor
/// argument, by an array literal, by a call `with owned`, and -- for a linear or affine value -- by
/// `Drop()`, but not by any other field taking it, as classic has it; a use after that is refused at the
/// use, and a move inside a loop of a binding that outlives the loop is refused at the move. A lease is a
/// local declared frozen, borrowed or readonly ref from a place, and ends where that local's block does,
/// which is what the Leave step is for. What a place may do depends on three things, judged in this order
/// (owned-fields Requirement 16): its own state and that of the places above it, since a lease on the
/// holder is a lease on every field it holds; and, for an action on the whole of it, the worst state
/// among the places below it, which is the composite restriction -- a holder with a field moved out is
/// incomplete and cannot be handed anywhere, while its other fields stay usable and the moved one may be
/// written back. A write to a slot is the one action not evaluated as a step, so its own borrow is
/// judged there; everything else complains about a borrow where the place is read. What such a store
/// keeps, like an owned field stored into a plain one, is a second holder of the value, and so is an
/// ordinary parameter -- one neither owned nor lent -- since the callee may keep what it is handed
/// (owned-fields Requirement 18.9), where a borrowed, frozen or readonly ref parameter gives it back as
/// the call returns; from there on the holder may not move into an owned destination or be returned
/// owned until it or that field is assigned afresh, and a plain use of it stays admitted. Two
/// paths join by remembering either path's move, which is classic's verdict measured; a use once
/// complained of settles the place, so one mistake is one complaint per path; and a struct is never
/// tracked, since a copy of it is a value of its own. The rest of what classic admits here -- a call as
/// no move for a linear value, a frozen lender lent borrowed by call -- remains under MIST-122 and
/// MIST-061 in backlog/issues.yaml.
/// </summary>
internal sealed partial class OwnershipFlow(
    AnalysisContext context,
    ThisTiers tiers,
    IReadOnlyDictionary<LocalVariable, Block> declaringBlocks,
    IReadOnlyDictionary<Block, LocalReferenceExpression> scoped,
    IReadOnlyDictionary<object, object> parents,
    Method? owning) : IFlowLattice<OwnershipFlow.State>
{
    public static readonly State Nothing = new(
        ImmutableDictionary<Place, Moved>.Empty,
        ImmutableDictionary.Create<LocalVariable, (Place, Lease)>(ReferenceEqualityComparer.Instance),
        ImmutableHashSet<Place>.Empty,
        ImmutableDictionary.Create<LocalVariable, Kept>(ReferenceEqualityComparer.Instance));

    internal enum Lease
    {
        ReadonlyRef,
        Frozen,
        Borrowed,
    }

    internal enum Held
    {
        Live,
        ReadonlyRef,
        Frozen,
        Borrowed,
        Moved,
    }

    private enum Action
    {
        Read,
        Write,
        View,
        Freeze,
        Borrow,
        Move,
    }

    internal sealed record Moved(object Node, bool Dropped);

    internal sealed record Kept(string Reason, string? Field);

    internal sealed record State(
        ImmutableDictionary<Place, Moved> Moved,
        ImmutableDictionary<LocalVariable, (Place Lender, Lease Kind)> Leases,
        ImmutableHashSet<Place> Complained,
        ImmutableDictionary<LocalVariable, Kept> Shared);

    public State Join(State left, State right)
    {
        var moved = left.Moved;

        foreach(var (place, move) in right.Moved)
        {
            if(!moved.ContainsKey(place))
            {
                moved = moved.Add(place, move);
            }
        }

        var leases = left.Leases;

        foreach(var (recipient, lease) in right.Leases)
        {
            if(!leases.ContainsKey(recipient))
            {
                leases = leases.Add(recipient, lease);
            }
        }

        var shared = left.Shared;

        foreach(var (local, kept) in right.Shared)
        {
            if(!shared.ContainsKey(local))
            {
                shared = shared.Add(local, kept);
            }
        }

        return new State(moved, leases, left.Complained.Union(right.Complained), shared);
    }

    public bool Same(State left, State right)
    {
        return left.Moved.Count == right.Moved.Count
            && left.Moved.Keys.All(right.Moved.ContainsKey)
            && left.Leases.Count == right.Leases.Count
            && left.Leases.Keys.All(right.Leases.ContainsKey)
            && left.Complained.SetEquals(right.Complained)
            && left.Shared.Count == right.Shared.Count
            && left.Shared.Keys.All(right.Shared.ContainsKey);
    }

    public State Transfer(State state, FlowStep step)
    {
        switch(step)
        {
            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration }:
                state = Forget(state, declaration.Local);

                if(declaration.Initializer is not { } initializer)
                {
                    return state;
                }

                if(Place.Of(initializer) is not { } source)
                {
                    return state;
                }

                if(LeaseOf(declaration.Local) is { } lease)
                {
                    return state with { Leases = state.Leases.SetItem(declaration.Local, (source, lease)) };
                }

                return Move(state, source, initializer);

            case { Kind: FlowStepKind.Evaluate, Node: Expression read } when Place.Of(read) is { } place:
                if(state.Moved.ContainsKey(place))
                {
                    return state with { Moved = state.Moved.Remove(place) };
                }

                return Has(state, place, Lease.Borrowed) ? state with { Complained = state.Complained.Add(place) } : state;

            case { Kind: FlowStepKind.Leave, Node: Block block }:
                if(scoped.TryGetValue(block, out var given))
                {
                    state = state with { Moved = state.Moved.SetItem(Place.Of(given.Local), new Moved(given, Dropped: true)) };
                }

                return state with
                {
                    Leases = state.Leases.RemoveRange(
                        state.Leases.Keys.Where(recipient => declaringBlocks.TryGetValue(recipient, out var home) && ReferenceEquals(home, block))),
                };

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false, Target: LocalReferenceExpression target } assignment }:
                if(OwnershipAnalyzer.Tracked(target.Local))
                {
                    state = Forget(state, target.Local);
                }

                return Place.Of(assignment.Value) is { } moved ? Move(state, moved, assignment.Value) : state;

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression assignment }:
                foreach(var (target, value) in Deconstructions.Pairs(assignment))
                {
                    state = Stored(state, target, value);
                }

                return state;

            case { Kind: FlowStepKind.Evaluate, Node: TupleLiteralExpression tuple }:
                return tuple.Elements.Aggregate(state, (kept, element) => Keep(kept, element, "a tuple"));

            case { Kind: FlowStepKind.Evaluate, Node: AnonymousObjectExpression anonymous }:
                return anonymous.Values.Aggregate(state, (kept, value) => Keep(kept, value, "an anonymous object"));

            case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression invocation }:
                state = HandArguments(state, invocation.Arguments, invocation.Parameters, invocation.Method is { } called ? $"'{called.Name}'" : "a function value");
                state = MoveArguments(state, invocation.Arguments, invocation.Parameters);

                if(ThisTiers.ReceiverOf(invocation) is not { } receiver || Place.Of(receiver) is not { } held || !Place.Tracked(held))
                {
                    return state;
                }

                if(invocation.Method?.DeclaredThisTier == ThisTier.Owned || IsDrop(invocation))
                {
                    return state with { Moved = Without(state.Moved, held).SetItem(held, new Moved(invocation.Node, IsDrop(invocation))) };
                }

                return invocation.Method is { } method ? Settle(state, held, method) : state;

            case { Kind: FlowStepKind.Evaluate, Node: NewExpression creation }:
                state = creation.Initializer.Where(entry => !entry.IsTargetFailed).Aggregate(state, (kept, entry) => Keep(kept, entry.Value, $"'{entry.MemberName}'"));
                state = HandArguments(state, creation.Arguments, creation.Constructor?.ParametersList, $"'{creation.Type.Name}'");
                return MoveArguments(state, creation.Arguments, creation.Constructor?.ParametersList);

            case { Kind: FlowStepKind.Evaluate, Node: ArrayLiteralExpression literal }:
                foreach(var element in literal.Elements)
                {
                    if(Place.Of(element) is { } place)
                    {
                        state = Move(state, place, element);
                    }
                }

                return state;

            default:
                return state;
        }
    }

    private static State Stored(State state, Expression target, Expression value)
    {
        if(target is LocalReferenceExpression)
        {
            return state;
        }

        if(Place.Of(target) is { } slot)
        {
            state = state with { Moved = Without(state.Moved, slot).Remove(slot) };
        }

        if(target is MemberAccessExpression { Receiver: LocalReferenceExpression { Local: var holder }, Member: FieldResult { Field.Name: var restored } }
            && state.Shared.TryGetValue(holder, out var partial) && partial.Field == restored)
        {
            state = state with { Shared = state.Shared.Remove(holder) };
        }

        if(ThisTiers.OwnedSlot(target) is not null && Place.Of(value) is { } moved)
        {
            return Move(state, moved, value);
        }

        return Keep(state, value, Path(target) is { } path ? $"'{path}'" : "a field");
    }

    private static State Keep(State state, Expression value, string where)
    {
        return HolderOf(value) switch
        {
            (var local, null) => Share(state, local, $"'{local.Name}' is also held by {where}, where it was stored without a move", null),
            (var holder, { } field) => Share(state, holder, $"its owned field '{field.Name}' is also held by {where}, which read it out without a move", field.Name),
            null => state,
        };
    }

    private static State HandArguments(State state, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, string callee)
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            var parameter = parameters switch
            {
                null => null,
                _ when i < parameters.Count => parameters[i],
                [.., { IsFlattened: true } last] => last,
                _ => null,
            };

            if(parameters is null)
            {
                state = Hand(state, arguments[i], callee);
            }
            else if(parameter is not null && Hands(parameter.Modifier))
            {
                state = Hand(state, arguments[i], $"parameter '{parameter.Name}' of {callee}");
            }
        }

        return state;
    }

    private static bool Hands(ParameterModifier modifier)
    {
        return modifier is { Ownership: OwnershipKind.Unrestricted, Direction: not ByRefDirection.Out } and not { IsRef: true, IsReadonly: true };
    }

    private static State Hand(State state, Expression argument, string recipient)
    {
        return HolderOf(argument) switch
        {
            (var local, null) => Share(state, local, $"'{local.Name}' was handed to {recipient} without a move, so another holder may still reach it", null),
            (var holder, { } field) => Share(state, holder, $"its owned field '{field.Name}' was handed to {recipient} without a move, so another holder may still reach it", field.Name),
            null => state,
        };
    }

    private static State Share(State state, LocalVariable holder, string reason, string? field)
    {
        return state with { Shared = state.Shared.SetItem(holder, new Kept(reason, field)) };
    }

    private static (LocalVariable Holder, Field? Field)? HolderOf(Expression value)
    {
        return OwnershipAnalyzer.Unwrapped(value) switch
        {
            LocalReferenceExpression { Local: { Ownership: OwnershipKind.Owned } local }
                when OwnershipAnalyzer.Tracked(local) && !Origins.IsUnchangeable(local.Type) => (local, null),
            MemberAccessExpression { Receiver: LocalReferenceExpression { Local: { Ownership: OwnershipKind.Owned } holder }, Member: FieldResult { Field: var field } }
                when OwnershipAnalyzer.Tracked(holder) && ThisTiers.IsOwned(field) && !Origins.IsUnchangeable(field.Type) => (holder, field),
            _ => null,
        };
    }

    private static string? Path(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression local => local.Local.Name,
            ThisExpression => "this",
            FieldReferenceExpression field => field.Field.Name,
            PropertyReferenceExpression property => property.Property.Name,
            MemberAccessExpression access => Path(access.Receiver) is { } receiver ? $"{receiver}.{access.MemberName}" : access.MemberName,
            IndexExpression index => Path(index.Receiver) is { } receiver ? $"{receiver}[]" : null,
            _ => null,
        };
    }

    private static State MoveArguments(State state, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        for(var i = 0; i < arguments.Count && parameters is not null && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.Ownership == OwnershipKind.Owned && Place.Of(arguments[i]) is { } place)
            {
                state = Move(state, place, arguments[i]);
            }
        }

        return state;
    }

    private static State Move(State state, Place place, Expression at)
    {
        return Place.Tracked(place)
            ? state with { Moved = Without(state.Moved, place).SetItem(place, new Moved(at, Dropped: false)) }
            : state;
    }

    private State Settle(State state, Place held, Method method)
    {
        var touched = tiers.FieldUses(method).Keys.Where(Place.Holds).Select(held.Child).ToList();

        return state with { Moved = state.Moved.RemoveRange(state.Moved.Keys.Where(moved => touched.Exists(child => child.Equals(moved) || child.IsAncestorOf(moved)))) };
    }

    private static State Forget(State state, LocalVariable local)
    {
        return state with
        {
            Moved = state.Moved.RemoveRange(state.Moved.Keys.Where(place => ReferenceEquals(place.Root, local))),
            Shared = state.Shared.Remove(local),
        };
    }

    private static ImmutableDictionary<Place, Moved> Without(ImmutableDictionary<Place, Moved> moved, Place place)
    {
        return moved.RemoveRange(moved.Keys.Where(place.IsAncestorOf));
    }

    private static bool Has(State state, Place place, Lease kind)
    {
        return state.Leases.Values.Any(lease => lease.Lender.Equals(place) && lease.Kind == kind);
    }

    private static Lease? LeaseOf(LocalVariable recipient)
    {
        return recipient switch
        {
            { Ownership: OwnershipKind.Frozen } => Lease.Frozen,
            { Ownership: OwnershipKind.Borrowed } => Lease.Borrowed,
            { IsRef: true, IsReadonly: true } => Lease.ReadonlyRef,
            _ => null,
        };
    }

    private static bool IsDrop(InvocationExpression invocation)
    {
        return invocation.Method is { Name: "Drop", ParametersList.Count: 0 }
            && ThisTiers.ReceiverOf(invocation) is { } receiver
            && OwnershipAnalyzer.Substructural(receiver.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine;
    }
}