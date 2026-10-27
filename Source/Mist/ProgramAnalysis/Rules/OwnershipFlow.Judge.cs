using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class OwnershipFlow
{
    public void Judge(FlowGraph graph, State entry)
    {
        var solution = FlowSolution<State>.Solve(graph, this, entry);
        solution.Replay((state, step) => Judge(graph, state, step));

        foreach(var loop in graph.Loops)
        {
            if(!solution.Reaches(loop.Latch))
            {
                continue;
            }

            var inside = BodyWalker.DescendantsAndSelf(loop.Body).ToHashSet(ReferenceEqualityComparer.Instance);
            var state = loop.Latch.Steps.Aggregate(solution.At(loop.Latch), Transfer);

            foreach(var (place, move) in state.Moved)
            {
                if(move.Node is Expression { Node: { } at } expression && inside.Contains(expression)
                    && !(declaringBlocks.TryGetValue(place.Root, out var home) && inside.Contains(home)))
                {
                    context.Report(ProgramAnalysisDiagnostics.LoopMove, at, [expression], new NameValue(place.Display));
                }
            }
        }
    }

    private void Judge(FlowGraph graph, State state, FlowStep step)
    {
        switch(step)
        {
            case { Kind: FlowStepKind.Evaluate, Node: Expression read } when Place.Of(read) is { } place:
                JudgeRead(state, read, place);
                break;

            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: { } initializer } declaration }
                when Place.Of(initializer) is { } source:
                if(LeaseOf(declaration.Local) is { } lease)
                {
                    JudgeAction(state, source, initializer, lease switch { Lease.Borrowed => Action.Borrow, Lease.Frozen => Action.Freeze, _ => Action.View });
                }
                else if(Place.Tracked(source))
                {
                    JudgeAction(state, source, initializer, Action.Move);
                    JudgeShared(state, initializer, $"owned binding '{declaration.Local.Name}'", declaration.Local.Ownership == OwnershipKind.Owned);
                }

                break;

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false } assignment }:
                JudgeAssignment(state, assignment);
                break;

            case { Kind: FlowStepKind.Evaluate, Node: ReturnStatement { Value: { } returned } }
                when owning is not null && SharedThrough(state, returned) is { } kept:
                context.Report(ProgramAnalysisDiagnostics.OwnedReturnNotOwned, returned.Node, [returned], new NameValue(owning.Name), new NameValue(kept.Reason));
                break;

            case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression invocation }:
                JudgeArguments(state, invocation.Arguments, invocation.Parameters);

                if(ThisTiers.ReceiverOf(invocation) is { } receiver && Place.Of(receiver) is { } held)
                {
                    JudgeReceiver(state, invocation, receiver, held);
                }

                break;

            case { Kind: FlowStepKind.Evaluate, Node: NewExpression creation }:
                JudgeArguments(state, creation.Arguments, creation.Constructor?.ParametersList);
                break;

            case { Kind: FlowStepKind.Evaluate, Node: LambdaExpression or AsyncBlockExpression or LocalFunctionStatement } when graph.Nested.TryGetValue(step.Node, out var nested):
                new OwnershipFlow(context, tiers, declaringBlocks, scoped, parents, owning: null).Judge(nested, state);
                break;
        }
    }

    private void JudgeRead(State state, Expression read, Place place)
    {
        if(state.Moved.TryGetValue(place, out var move))
        {
            if(!ReferenceEquals(move.Node, read) && !IsReceiverOf(move.Node, read))
            {
                context.Report(ProgramAnalysisDiagnostics.UseAfterMove, read.Node, [read], new NameValue(place.Display), new EnumValue(move.Dropped ? "dropped" : "moved"));
            }
        }
        else if(Has(state, place, Lease.Borrowed))
        {
            if(!state.Complained.Contains(place))
            {
                context.Report(ProgramAnalysisDiagnostics.LeaseViolation, read.Node, [read], new NameValue(place.Display), new EnumValue("access"), new EnumValue("borrowed"));
            }
        }
        else if(Whole(read) && !Consumed(read))
        {
            JudgeComposite(state, place, read, Action.Read);
        }
    }

    private void JudgeAssignment(State state, AssignmentExpression assignment)
    {
        foreach(var (target, value) in Deconstructions.Pairs(assignment))
        {
            if(target is LocalReferenceExpression written)
            {
                if(Place.Of(value) is { } moved && Place.Tracked(moved))
                {
                    JudgeAction(state, moved, value, Action.Move);
                    JudgeShared(state, value, $"owned binding '{written.Local.Name}'", written.Local.Ownership == OwnershipKind.Owned);
                }

                continue;
            }

            if(Place.Of(target) is { } slot)
            {
                JudgeAction(state, slot, target, Action.Write, evaluated: false);
            }
            else if(target is MemberAccessExpression { Receiver: var receiver } && Place.Nearest(receiver) is { } holder
                && Lent(state, holder, Action.Write, evaluated: true) is var (lent, kind))
            {
                context.Report(ProgramAnalysisDiagnostics.LeaseViolation, receiver.Node, [receiver], new NameValue(lent.Display), new EnumValue("mutate"), new EnumValue(Display(kind)));
            }

            if(ThisTiers.OwnedSlot(target) is { } destination && Place.Of(value) is { } stored && Place.Tracked(stored))
            {
                JudgeAction(state, stored, value, Action.Move);
                JudgeShared(state, value, destination, owned: true);
            }
        }
    }

    private void JudgeArguments(State state, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        for(var i = 0; i < arguments.Count && parameters is not null && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.Ownership == OwnershipKind.Owned && Place.Of(arguments[i]) is { } place && Place.Tracked(place))
            {
                JudgeAction(state, place, arguments[i], Action.Move);
                JudgeShared(state, arguments[i], $"owned parameter '{parameters[i].Name}'", owned: true);
            }
        }
    }

    private void JudgeReceiver(State state, InvocationExpression invocation, Expression receiver, Place held)
    {
        if(invocation.Method is not { } method)
        {
            return;
        }

        if(tiers.Required(method) >= ThisTier.Borrowed && Lent(state, held, Action.Write, evaluated: true) is var (lent, kind))
        {
            context.Report(ProgramAnalysisDiagnostics.LeaseViolation, receiver.Node, [receiver], new NameValue(lent.Display), new EnumValue("mutate"), new EnumValue(Display(kind)));
        }
        else if(method.DeclaredThisTier == ThisTier.Owned && Place.Tracked(held))
        {
            JudgeAction(state, held, receiver, Action.Move);
            JudgeShared(state, receiver, $"the receiver of '{method.Name}', which it takes owned", owned: true);
        }
        else if(StateOf(state, held) != Held.Live)
        {
            JudgeFieldUses(state, invocation, method, receiver, held);
        }
    }

    private void JudgeFieldUses(State state, InvocationExpression invocation, Method method, Expression receiver, Place held)
    {
        foreach(var (field, use) in tiers.FieldUses(method))
        {
            if(!Place.Holds(field))
            {
                continue;
            }

            var child = held.Child(field);
            var current = StateOf(state, child);
            var refused = current switch
            {
                Held.Moved or Held.Borrowed => true,
                Held.Frozen => use.HasFlag(FieldUse.Writes),
                _ => false,
            };

            if(refused)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.FieldStateCall, invocation.Node, [invocation, receiver],
                    new NameValue(method.Name), new NameValue(held.Display), new EnumValue(use.HasFlag(FieldUse.Writes) ? "writes" : "reads"), new NameValue(field.Name), new EnumValue(Describe(current)));
                return;
            }
        }
    }

    private void JudgeAction(State state, Place place, Expression at, Action action, bool evaluated = true)
    {
        if(Lent(state, place, action, evaluated) is var (lent, kind))
        {
            context.Report(ProgramAnalysisDiagnostics.LeaseViolation, at.Node, [at], new NameValue(lent.Display), new EnumValue(Verb(action)), new EnumValue(Display(kind)));
            return;
        }

        JudgeComposite(state, place, at, action);
    }

    private void JudgeComposite(State state, Place place, Expression at, Action action)
    {
        if(Blocking(state, place, action) is var (child, held))
        {
            context.Report(ProgramAnalysisDiagnostics.CompositeState, at.Node, [at], new EnumValue(Verb(action)), new NameValue(place.Display), new NameValue(child.RelativeTo(place)), new EnumValue(Describe(held)));
        }
    }

    private void JudgeShared(State state, Expression source, string destination, bool owned)
    {
        if(owned && SharedThrough(state, source) is { } kept)
        {
            context.Report(ProgramAnalysisDiagnostics.OwnedSourceNotOwned, source.Node, [source], new NameValue(destination), new NameValue(kept.Reason));
        }
    }

    private static Kept? SharedThrough(State state, Expression source)
    {
        if(Place.Of(OwnershipAnalyzer.Unwrapped(source)) is not { } place || !state.Shared.TryGetValue(place.Root, out var kept))
        {
            return null;
        }

        return place.IsRoot || kept.Field is null || kept.Field == place.Path[0].Name ? kept : null;
    }

    private static (Place Lent, Lease Kind)? Lent(State state, Place place, Action action, bool evaluated)
    {
        foreach(var candidate in place.AncestorsAndSelf())
        {
            foreach(var (lender, kind) in state.Leases.Values)
            {
                if(lender.Equals(candidate) && Forbids(kind, action, self: candidate.Equals(place) && !evaluated))
                {
                    return (candidate, kind);
                }
            }
        }

        return null;
    }

    private static bool Forbids(Lease kind, Action action, bool self)
    {
        return kind switch
        {
            Lease.Frozen => action is Action.Write or Action.Borrow or Action.Move,
            Lease.ReadonlyRef => action is Action.Borrow or Action.Move,
            _ => self && action == Action.Write,
        };
    }

    private static (Place Child, Held Held)? Blocking(State state, Place place, Action action)
    {
        (Place Child, Held Held)? worst = null;

        foreach(var (child, _) in state.Moved)
        {
            worst = Prefer(worst, place, child, Held.Moved, action);
        }

        foreach(var (lender, kind) in state.Leases.Values)
        {
            worst = Prefer(worst, place, lender, kind switch { Lease.Borrowed => Held.Borrowed, Lease.Frozen => Held.Frozen, _ => Held.ReadonlyRef }, action);
        }

        return worst;
    }

    private static (Place, Held)? Prefer((Place Child, Held Held)? worst, Place place, Place child, Held held, Action action)
    {
        if(!place.IsAncestorOf(child) || !Blocks(held, action) || (worst is var (_, known) && known >= held))
        {
            return worst;
        }

        return (child, held);
    }

    private static bool Blocks(Held held, Action action)
    {
        return held switch
        {
            Held.Moved => action != Action.Write,
            Held.Borrowed => true,
            Held.Frozen => action is Action.Write or Action.Borrow or Action.Move,
            Held.ReadonlyRef => action is Action.Borrow or Action.Move,
            _ => false,
        };
    }

    private static Held StateOf(State state, Place place)
    {
        var held = Held.Live;

        foreach(var (moved, _) in state.Moved)
        {
            if(moved.Equals(place) || place.IsAncestorOf(moved))
            {
                held = Held.Moved;
            }
        }

        foreach(var (lender, kind) in state.Leases.Values)
        {
            if(lender.Equals(place) || place.IsAncestorOf(lender))
            {
                var lent = kind switch { Lease.Borrowed => Held.Borrowed, Lease.Frozen => Held.Frozen, _ => Held.ReadonlyRef };
                held = lent > held ? lent : held;
            }
        }

        return held;
    }

    private bool Whole(Expression read)
    {
        return !(parents.TryGetValue(read, out var parent) && parent switch
        {
            MemberAccessExpression access => ReferenceEquals(access.Receiver, read),
            FunctionReferenceExpression function => ReferenceEquals(function.Receiver, read),
            MethodGroupExpression group => ReferenceEquals(group.Receiver, read),
            IndexExpression index => ReferenceEquals(index.Receiver, read),
            ExtensionPropertyAccessExpression extension => ReferenceEquals(extension.Receiver, read),
            _ => false,
        });
    }

    private bool Consumed(Expression read)
    {
        return parents.TryGetValue(read, out var parent) && parent switch
        {
            LocalDeclarationStatement declaration => ReferenceEquals(declaration.Initializer, read),
            AssignmentExpression assignment => ReferenceEquals(assignment.Value, read)
                && (assignment.Target is LocalReferenceExpression || ThisTiers.OwnedSlot(assignment.Target) is not null),
            InvocationExpression invocation => TakesOwned(invocation.Arguments, invocation.Parameters, read),
            NewExpression creation => TakesOwned(creation.Arguments, creation.Constructor?.ParametersList, read),
            _ => false,
        };
    }

    private static bool TakesOwned(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, Expression read)
    {
        for(var i = 0; parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(ReferenceEquals(arguments[i], read))
            {
                return parameters[i].Modifier.Ownership == OwnershipKind.Owned;
            }
        }

        return false;
    }

    private static bool IsReceiverOf(object move, Expression read)
    {
        return move is InvocationExpression invocation && ReferenceEquals(ThisTiers.ReceiverOf(invocation), read);
    }

    private static string Verb(Action action)
    {
        return action switch
        {
            Action.Read => "read",
            Action.Write => "mutate",
            Action.View => "view",
            Action.Freeze => "freeze",
            Action.Borrow => "borrow",
            _ => "move",
        };
    }

    private static string Display(Lease kind)
    {
        return kind switch
        {
            Lease.Frozen => "frozen",
            Lease.ReadonlyRef => "readonly ref",
            _ => "borrowed",
        };
    }

    private static string Describe(Held held)
    {
        return held switch
        {
            Held.Moved => "moved",
            Held.Borrowed => "lent borrowed",
            Held.Frozen => "lent frozen",
            _ => "lent readonly ref",
        };
    }
}