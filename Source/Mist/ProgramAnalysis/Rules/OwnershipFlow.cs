using System.Collections.Immutable;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Judge a <see cref="Place"/>'s own and ancestor state first, then its worst descendant state for whole-place
/// actions (owned-fields Requirement 16):
/// a holder's lease covers its fields, and a moved field prevents whole-holder use while siblings remain
/// usable and the field can be restored. Leases end with the recipient's block. A slot write has no
/// evaluation step, so its borrow is checked at the store; other actions report at the read.
/// A plain field store or ordinary parameter may retain another holder (Requirement 18.9); borrowed,
/// frozen and readonly ref parameters return the loan. Shared holders stay usable but cannot move or
/// return owned until the holder or affected field is assigned afresh. Joins remember either path's
/// move; a reported use settles the place on that path. Linear and affine structs move like classes;
/// ordinary structs copy even in owned bindings. Non-owned field stores keep rather than move.
/// Calls that do not move linear values and frozen lenders lent borrowed by call remain under
/// MIST-122 and MIST-061 in Carbonek.
/// </summary>
internal sealed partial class OwnershipFlow(
    AnalysisContext context,
    ThisTiers tiers,
    IReadOnlyDictionary<LocalVariable, Block> declaringBlocks,
    WithScopes scopes,
    IReadOnlyDictionary<object, object> parents,
    Method? owning,
    LocalOwnership? inferred = null) : IFlowLattice<OwnershipFlow.State>
{
    private readonly LocalOwnership locals = inferred ?? LocalOwnership.Empty;

    public static readonly State Nothing = new(
        ImmutableDictionary<Place, Moved>.Empty,
        ImmutableDictionary.Create<LocalVariable, ImmutableHashSet<Loan>>(ReferenceEqualityComparer.Instance),
        ImmutableHashSet<Place>.Empty,
        ImmutableDictionary.Create<LocalVariable, Kept>(ReferenceEqualityComparer.Instance),
        ImmutableDictionary<Block, ImmutableHashSet<object>>.Empty,
        ImmutableDictionary<Place, ImmutableHashSet<object>>.Empty);

    internal enum Lease
    {
        ReadonlyRef,
        Ref,
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

    internal sealed record Loan(Place Lender, Lease Kind, object Value);

    internal sealed record State(
        ImmutableDictionary<Place, Moved> Moved,
        ImmutableDictionary<LocalVariable, ImmutableHashSet<Loan>> Leases,
        ImmutableHashSet<Place> Complained,
        ImmutableDictionary<LocalVariable, Kept> Shared,
        ImmutableDictionary<Block, ImmutableHashSet<object>> Installed,
        ImmutableDictionary<Place, ImmutableHashSet<object>> Values);

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
            leases = leases.SetItem(recipient, leases.TryGetValue(recipient, out var prior) ? prior.Union(lease) : lease);
        }

        var shared = left.Shared;

        foreach(var (local, kept) in right.Shared)
        {
            if(!shared.ContainsKey(local))
            {
                shared = shared.Add(local, kept);
            }
        }

        return new State(moved, leases, left.Complained.Union(right.Complained), shared, MergeValues(left.Installed, right.Installed), JoinValues(left.Values, right.Values));
    }

    public bool Same(State left, State right)
    {
        return left.Moved.Count == right.Moved.Count
            && left.Moved.Keys.All(right.Moved.ContainsKey)
            && left.Leases.Count == right.Leases.Count
            && left.Leases.All(pair => right.Leases.TryGetValue(pair.Key, out var loans) && pair.Value.SetEquals(loans))
            && left.Complained.SetEquals(right.Complained)
            && left.Shared.Count == right.Shared.Count
            && left.Shared.Keys.All(right.Shared.ContainsKey)
            && SameValues(left.Installed, right.Installed)
            && SameValues(left.Values, right.Values);
    }

    public State Transfer(State state, FlowStep step)
    {
        if(SnapshotAt(step) is { } snapshot)
        {
            return state with { Moved = Without(state.Moved, snapshot.Place).SetItem(snapshot.Place, new Moved(snapshot.Pattern, Dropped: false)) };
        }

        if(step is { Kind: FlowStepKind.Evaluate, Node: Expression expression }
            && expression is not (ResultIdentityExpression or ElementReadExpression) && ResultTransfers.Source(expression) is { } transfer && Place.Of(transfer) is { } transferred)
        {
            return Move(state, transferred, expression);
        }

        switch(step)
        {
            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration }:
                state = BindValue(state, declaration.Local, declaration.Initializer, declaration);

                if(declaration.Initializer is not { } initializer)
                {
                    return state;
                }

                var sources = LocalOwnership.Sources(initializer).ToList();
                if(LeaseOf(declaration.Local) is { } lease)
                {
                    var loans = sources.SelectMany(source => ValuesOf(state.Values, source).Select(value => new Loan(source, lease, value))).ToImmutableHashSet();
                    return state with { Leases = state.Leases.SetItem(declaration.Local, loans) };
                }

                return declaration.Local.IsRef ? state : sources.Where(source => MovesInto(declaration.Local, source)).Aggregate(state, (current, source) => Move(current, source, initializer));

            case { Kind: FlowStepKind.Evaluate, Node: Expression read } when Place.Of(read) is { } place:
                if(state.Moved.ContainsKey(place))
                {
                    state = state with { Moved = state.Moved.Remove(place) };
                }
                else if(Has(state, place, Lease.Borrowed))
                {
                    state = state with { Complained = state.Complained.Add(place) };
                }

                return Install(state, read);

            case { Kind: FlowStepKind.Bind, Variable: { } bound }:
                return BindValue(state, bound, PatternSource(step.Node), step.Node);

            case { Kind: FlowStepKind.Introduce, Node: WithBlockStatement introduced, Variable: { } binding }:
                return Install(BindValue(state, binding, introduced.Value, introduced), introduced);

            case { Kind: FlowStepKind.Leave, Node: Block block }:
                state = LeaveInstallation(state, block);

                return state with
                {
                    Leases = state.Leases.RemoveRange(
                        state.Leases.Keys.Where(recipient => declaringBlocks.TryGetValue(recipient, out var home) && ReferenceEquals(home, block))),
                };

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false, Target: LocalReferenceExpression target } assignment }:
                if(Tracked(Place.Of(target.Local)))
                {
                    state = BindValue(state, target.Local, assignment.Value, assignment, assignment: true);
                }

                return LocalOwnership.Sources(assignment.Value).Aggregate(state, (current, source) => Move(current, source, assignment.Value));

            case { Kind: FlowStepKind.Evaluate, Node: CompoundAssignmentExpression { Target: LocalReferenceExpression target } compound }:
                return Tracked(Place.Of(target.Local)) ? BindValue(state, target.Local, compound, compound, assignment: true) : state;

            case { Kind: FlowStepKind.Evaluate, Node: CompoundAssignmentExpression compound }:
                if(LocalStoragePath.Of(compound.Target) is { Receiver: not null } written)
                {
                    state = state with { Moved = state.Moved.Remove(Place.Of(written.Local)) };
                }

                return Stored(state, compound.Target, compound);

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression assignment }:
                foreach(var (target, value) in Deconstructions.Pairs(assignment))
                {
                    if(LocalStoragePath.Of(target) is { Receiver: not null } storage)
                    {
                        state = state with { Moved = state.Moved.Remove(Place.Of(storage.Local)) };
                    }

                    state = Stored(state, target, value);
                }

                return state;

            case { Kind: FlowStepKind.Evaluate, Node: ReturnStatement { Value: { } returned } }:
                return LocalOwnership.Sources(returned).Aggregate(state, (current, source) => Move(current, source, returned));

            case { Kind: FlowStepKind.Evaluate, Node: TupleLiteralExpression tuple }:
                return tuple.Elements.Aggregate(state, (kept, element) => Keep(kept, element, "a tuple"));

            case { Kind: FlowStepKind.Evaluate, Node: AnonymousObjectExpression anonymous }:
                return anonymous.Values.Aggregate(state, (kept, value) => Keep(kept, value, "an anonymous object"));

            case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression invocation }:
                state = HandArguments(state, invocation.Arguments, invocation.Parameters, invocation.Method is { } called ? $"'{called.Name}'" : "a function value");
                state = MoveArguments(state, invocation.Arguments, invocation.Parameters);

                if(ThisTiers.ReceiverOf(invocation) is { } receiver)
                {
                    foreach(var held in LocalOwnership.Sources(receiver).Where(Tracked))
                    {
                        if(invocation.Method?.DeclaredThisTier == ThisTier.Owned || IsDrop(invocation))
                        {
                            state = state with { Moved = Without(state.Moved, held).SetItem(held, new Moved(invocation.Node, IsDrop(invocation))) };
                        }
                        else if(invocation.Method is { } method)
                        {
                            state = Settle(state, held, method);
                        }
                    }
                }

                return state;

            case { Kind: FlowStepKind.Evaluate, Node: NewExpression creation }:
                state = HandArguments(state, creation.Arguments, creation.Constructor?.ParametersList, $"'{creation.Type.Name}'");
                return MoveArguments(state, creation.Arguments, creation.Constructor?.ParametersList);

            case { Kind: FlowStepKind.Evaluate, Node: ObjectInitializerAssignment { IsTargetFailed: false } entry }:
                if(entry.Add is { } add)
                {
                    state = HandArguments(state, [entry.Value], add.ParametersList, $"'{add.Name}'");
                    return MoveArguments(state, [entry.Value], add.ParametersList);
                }

                return OwnedProvenance.OwnershipOf(entry.Member) == OwnershipKind.Owned
                    ? LocalOwnership.Sources(entry.Value).Aggregate(state, (current, source) => Move(current, source, entry.Value))
                    : Keep(state, entry.Value, $"'{entry.MemberName}'");

            case { Kind: FlowStepKind.Evaluate, Node: ArrayLiteralExpression literal }:
                foreach(var element in literal.Elements)
                {
                    state = LocalOwnership.Sources(element).Aggregate(state, (current, source) => Move(current, source, element));
                }

                return state;

            default:
                return state;
        }
    }

    private State Stored(State state, Expression target, Expression value)
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

        if(ThisTiers.OwnedSlot(target) is not null)
        {
            return LocalOwnership.Sources(value).Aggregate(state, (current, source) => Move(current, source, value));
        }

        return Keep(state, value, Path(target) is { } path ? $"'{path}'" : "a field");
    }

    private State Keep(State state, Expression value, string where)
    {
        return HolderOf(value) switch
        {
            (var local, null) => Share(state, local, $"'{local.Name}' is also held by {where}, where it was stored without a move", null),
            (var holder, { } field) => Share(state, holder, $"its owned field '{field.Name}' is also held by {where}, which read it out without a move", field.Name),
            null => state,
        };
    }

    private State HandArguments(State state, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, string callee)
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

    private State Hand(State state, Expression argument, string recipient)
    {
        foreach(var source in LocalOwnership.Sources(argument))
        {
            var values = ValuesOf(state.Values, source);
            foreach(var holder in state.Values.Keys.Where(place => place.IsRoot && Tracked(place) && !Origins.IsUnchangeable(place.Root.Type)
                && ValuesOf(state.Values, place).Overlaps(values)))
            {
                state = Share(state, holder.Root, $"'{holder.Display}' was handed to {recipient} without a move, so another holder may still reach it", null);
            }
        }

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

    private (LocalVariable Holder, Field? Field)? HolderOf(Expression value)
    {
        return OwnershipAnalyzer.Unwrapped(value) switch
        {
            LocalReferenceExpression { Local: { Ownership: OwnershipKind.Owned } local }
                when Tracked(Place.Of(local)) && !Origins.IsUnchangeable(local.Type) => (local, null),
            MemberAccessExpression { Receiver: LocalReferenceExpression { Local: { Ownership: OwnershipKind.Owned } holder }, Member: FieldResult { Field: var field } }
                when Tracked(Place.Of(holder)) && ThisTiers.IsOwned(field) && !Origins.IsUnchangeable(field.Type) => (holder, field),
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

    private static bool ConsumesResultArgument(Expression argument, Parameter parameter)
    {
        return parameter.Modifier.Ownership == OwnershipKind.Owned
            || (ResultTransfers.Payloads(argument.Type).Count > 0 && !parameter.Modifier.IsRef
                && parameter.Modifier.Ownership is not (OwnershipKind.Borrowed or OwnershipKind.Frozen));
    }

    private State MoveArguments(State state, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        for(var i = 0; i < arguments.Count && parameters is not null && i < parameters.Count; i++)
        {
            if(ConsumesResultArgument(arguments[i], parameters[i]))
            {
                state = LocalOwnership.Sources(arguments[i]).Aggregate(state, (current, source) => Move(current, source, arguments[i]));
            }
        }

        return state;
    }

    private State Move(State state, Place place, Expression at)
    {
        return Tracked(place)
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
            Leases = state.Leases.Remove(local),
        };
    }

    private static ImmutableDictionary<Place, Moved> Without(ImmutableDictionary<Place, Moved> moved, Place place)
    {
        return moved.RemoveRange(moved.Keys.Where(place.IsAncestorOf));
    }

    private bool Has(State state, Place place, Lease kind)
    {
        return Loans(state, place).Any(lease => lease.Kind == kind);
    }

    private IEnumerable<Loan> Loans(State state, Place place)
    {
        var values = ValuesOf(state.Values, place);
        return state.Leases.Values.SelectMany(loans => loans).Where(loan => loan.Lender.Equals(place) && (loan.Kind is Lease.ReadonlyRef or Lease.Ref || values.Contains(loan.Value)));
    }

    private Lease? LeaseOf(LocalVariable recipient)
    {
        if(locals.IsLoan(recipient))
        {
            return locals.Of(recipient) switch
            {
                ThisTier.Borrowed => Lease.Borrowed,
                ThisTier.Frozen => Lease.Frozen,
                _ => Lease.ReadonlyRef,
            };
        }

        return recipient switch
        {
            { Ownership: OwnershipKind.Frozen } => Lease.Frozen,
            { Ownership: OwnershipKind.Borrowed } => Lease.Borrowed,
            { IsRef: true, IsReadonly: true } => Lease.ReadonlyRef,
            { IsRef: true } => Lease.Ref,
            _ => null,
        };
    }

    private bool MovesInto(LocalVariable recipient, Place source)
    {
        return Place.Tracked(source) || recipient.Ownership == OwnershipKind.Owned
            || (recipient.InfersOwnership && locals.Of(recipient) == ThisTier.Owned);
    }

    private bool Tracked(Place place)
    {
        return Place.Tracked(place) || (place.Root.InfersOwnership && locals.Of(place.Root) == ThisTier.Owned && !ThisTiers.IsValue(place.Root.Type) && !Origins.IsUnchangeable(place.Root.Type));
    }

    private static bool IsDrop(InvocationExpression invocation)
    {
        return invocation.Method is { Name: "Drop", ParametersList.Count: 0 }
            && ThisTiers.ReceiverOf(invocation) is { } receiver
            && OwnershipAnalyzer.Substructural(receiver.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine;
    }
}