using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Moves and leases on the flow framework. A binding is moved by a declaration or assignment that
/// takes it directly, by an owned parameter, an owned field or an owned constructor argument, by an
/// array literal, by a call `with owned`, and -- for a linear or affine value -- by `Drop()`, but not by
/// any other field taking it, as classic has it; a use after that is refused
/// at the use, and a move inside a loop of a binding that outlives the loop is refused at the move. What
/// such a store keeps, like an owned field read out of an owned binding, is a second holder of the
/// value, so from there on the binding may not move into an owned destination, which promises a value
/// nothing else holds, until it or that field is assigned afresh. A
/// lease is a local declared frozen, borrowed or readonly ref from another local, and ends where that
/// local's block does, which is what the Leave step is for: while it stands, the lender cannot be moved,
/// nor mutated while frozen, nor touched at all while borrowed. Two paths join by remembering either
/// path's move, which is classic's verdict measured; a use once complained of settles the binding, so
/// one mistake is one complaint per path; and a struct is never tracked, since a copy of it is a value
/// of its own. The rest of what classic admits here -- a call as no move for a linear
/// value, a frozen lender lent borrowed by call -- remains under MIST-122 and MIST-061 in backlog/issues.yaml.
/// <para>
/// An owned return hands the caller a value nothing else holds (ownership-v2 Requirements 1.5 and
/// 6.7), so a `return` in a method returning owned is judged by <see cref="Origins"/>, without flow: a
/// construction, a clone, a call returning owned, an owned binding, a local fresh at every assignment,
/// or a value of a type that cannot change; the receiver and its owned fields count once the method
/// takes `this` owned. Anything else is refused at the value.
/// </para>
/// </summary>
internal sealed class OwnershipAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        var tiers = new ThisTiers();

        foreach(var root in context.Bodies)
        {
            var graph = FlowGraphBuilder.Build(root.Root);
            var owning = OwningMethod(root);
            new Ownership(context, tiers, DeclaringBlocks(root.Root), owning).Judge(graph, Ownership.Nothing);

            if(owning is { Body: { } body })
            {
                JudgeOwnedReturns(context, owning, body, new Origins(body, owning, ScopeKind.Readonly));
            }

            OwnedDestinations.Judge(context, root);
        }

        foreach(var type in context.Types.OfType<ConcreteType>())
        {
            OwnedDestinations.JudgeDefaults(context, type);
        }
    }

    private static Method? OwningMethod(SourceRoot root)
    {
        return root.Member is Method { Body: { } body } method && ReferenceEquals(body, root.Root) && ReturnsOwned(method) ? method : null;
    }

    private static bool ReturnsOwned(Method method)
    {
        return method.Modifiers.IsOwned || method.ReturnOwnership == OwnershipKind.Owned;
    }

    private static void JudgeOwnedReturns(AnalysisContext context, Method method, object node, Origins origins)
    {
        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            if(child is LambdaExpression or LocalFunctionStatement or AsyncBlockExpression)
            {
                continue;
            }

            if(child is ReturnStatement { Value: { } value } && !YieldsOwned(value, method, origins))
            {
                context.Report(ProgramAnalysisDiagnostics.OwnedReturnNotOwned, value.Node, [value], method.Name, Unowned(value));
            }

            JudgeOwnedReturns(context, method, child, origins);
        }
    }

    internal static bool YieldsOwned(Expression value, Method? method, Origins origins)
    {
        if(Origins.IsUnchangeable(value.Type))
        {
            return true;
        }

        if(origins.Of(value) is Origin.Own)
        {
            return Unwrapped(value) is not LocalReferenceExpression { Local: { Ownership: not OwnershipKind.Owned } local } || !origins.Escapes(local);
        }

        return method?.DeclaredThisTier == ThisTier.Owned && Unwrapped(value) switch
        {
            ThisExpression => true,
            FieldReferenceExpression field => ThisTiers.IsOwned(field.Field),
            MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult found } => ThisTiers.IsOwned(found.Field),
            _ => false,
        };
    }

    internal static string? HeldByAnotherOwner(Expression value, Method? method)
    {
        var (field, receiver) = Unwrapped(value) switch
        {
            FieldReferenceExpression reference when ThisTiers.IsOwned(reference.Field) => (reference.Field.Name, null),
            MemberAccessExpression { Member: FieldResult found } access when ThisTiers.IsOwned(found.Field) => (found.Field.Name, access.Receiver),
            MemberAccessExpression { Member: PropertyResult { Property: { Modifiers: { IsOwned: true, IsStatic: false } } property } } access => (property.Name, access.Receiver),
            _ => (null, null),
        };

        return field is not null && !(method?.DeclaredThisTier == ThisTier.Owned && receiver is null or ThisExpression) ? field : null;
    }

    internal static string Unowned(Expression value)
    {
        return Unwrapped(value) switch
        {
            ThisExpression => "'this' is the receiver, which the caller still holds",
            LocalReferenceExpression { Local: { Parameter: not null } parameter } => $"parameter '{parameter.Name}' is not owned, so the caller may still hold its value",
            LocalReferenceExpression { Local: var local } => $"local '{local.Name}' is neither owned nor a fresh value nothing else keeps",
            FieldReferenceExpression field => $"field '{field.Field.Name}' is storage other references reach",
            MemberAccessExpression { Member: FieldResult found } => $"field '{found.Field.Name}' is storage other references reach",
            InvocationExpression { Method: { } called } => $"'{called.Name}' does not return owned",
            _ => "it is neither fresh, moved from an owned binding, nor of a type that cannot change",
        };
    }

    private static Expression Unwrapped(Expression expression)
    {
        return expression switch
        {
            ImplicitConversionExpression { Operator: null } conversion => Unwrapped(conversion.Operand),
            CastExpression { Operator: null } cast => Unwrapped(cast.Operand),
            AsExpression converted => Unwrapped(converted.Operand),
            _ => expression,
        };
    }

    private static Dictionary<LocalVariable, Block> DeclaringBlocks(object root)
    {
        var blocks = new Dictionary<LocalVariable, Block>(ReferenceEqualityComparer.Instance);

        foreach(var block in BodyWalker.DescendantsAndSelf(root).OfType<Block>())
        {
            foreach(var declaration in block.Statements.OfType<LocalDeclarationStatement>())
            {
                blocks[declaration.Local] = block;
            }
        }

        return blocks;
    }

    internal static bool Tracked(LocalVariable local)
    {
        return !ThisTiers.IsValue(local.Type)
            && (local.Ownership == OwnershipKind.Owned || Substructural(local.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine);
    }

    internal static SubstructuralDiscipline Substructural(IMistType type)
    {
        return (type is ConstructedType constructed ? constructed.Definition : type) is ConcreteType concrete
            ? concrete.Discipline
            : SubstructuralDiscipline.None;
    }

    private enum Lease
    {
        ReadonlyRef,
        Frozen,
        Borrowed,
    }

    private sealed record Moved(object Node, bool Dropped);

    private sealed record Kept(string Reason, string? Field);

    private sealed record State(
        ImmutableDictionary<LocalVariable, Moved> MovedBindings,
        ImmutableDictionary<LocalVariable, (LocalVariable Lender, Lease Kind)> Leases,
        ImmutableHashSet<LocalVariable> Complained,
        ImmutableDictionary<LocalVariable, Kept> Shared);

    private sealed class Ownership(
        AnalysisContext context,
        ThisTiers tiers,
        IReadOnlyDictionary<LocalVariable, Block> declaringBlocks,
        Method? owning) : IFlowLattice<State>
    {
        public static readonly State Nothing = new(
            ImmutableDictionary.Create<LocalVariable, Moved>(ReferenceEqualityComparer.Instance),
            ImmutableDictionary.Create<LocalVariable, (LocalVariable, Lease)>(ReferenceEqualityComparer.Instance),
            ImmutableHashSet.Create<LocalVariable>(ReferenceEqualityComparer.Instance),
            ImmutableDictionary.Create<LocalVariable, Kept>(ReferenceEqualityComparer.Instance));

        public State Join(State left, State right)
        {
            var moved = left.MovedBindings;

            foreach(var (local, move) in right.MovedBindings)
            {
                if(!moved.ContainsKey(local))
                {
                    moved = moved.Add(local, move);
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
            return left.MovedBindings.Count == right.MovedBindings.Count
                && left.MovedBindings.Keys.All(right.MovedBindings.ContainsKey)
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
                    state = state with { MovedBindings = state.MovedBindings.Remove(declaration.Local), Shared = state.Shared.Remove(declaration.Local) };

                    if(declaration.Initializer is { } initializer)
                    {
                        state = Keep(state, initializer, $"'{declaration.Local.Name}'", partOnly: true);
                    }

                    if(declaration.Initializer is not LocalReferenceExpression source)
                    {
                        return state;
                    }

                    if(LeaseOf(declaration.Local) is { } lease)
                    {
                        return state with { Leases = state.Leases.SetItem(declaration.Local, (source.Local, lease)) };
                    }

                    return Tracked(source.Local) ? Move(state, source.Local, declaration.Initializer) : state;

                case { Kind: FlowStepKind.Evaluate, Node: LocalReferenceExpression read }:
                    if(state.MovedBindings.ContainsKey(read.Local))
                    {
                        return state with { MovedBindings = state.MovedBindings.Remove(read.Local) };
                    }

                    return Has(state, read.Local, Lease.Borrowed) ? state with { Complained = state.Complained.Add(read.Local) } : state;

                case { Kind: FlowStepKind.Leave, Node: Block block }:
                    return state with
                    {
                        Leases = state.Leases.RemoveRange(
                            state.Leases.Keys.Where(recipient => declaringBlocks.TryGetValue(recipient, out var home) && ReferenceEquals(home, block))),
                    };

                case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false, Target: LocalReferenceExpression target } assignment }:
                    state = Keep(state, assignment.Value, $"'{target.Local.Name}'", partOnly: true);

                    if(Tracked(target.Local))
                    {
                        state = state with { MovedBindings = state.MovedBindings.Remove(target.Local), Shared = state.Shared.Remove(target.Local) };
                    }

                    return assignment.Value is LocalReferenceExpression moved && Tracked(moved.Local)
                        ? Move(state, moved.Local, assignment.Value)
                        : state;

                case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression assignment }:
                    foreach(var (target, value) in Deconstructions.Pairs(assignment))
                    {
                        state = Stored(state, target, value);
                    }

                    return state;

                case { Kind: FlowStepKind.Evaluate, Node: TupleLiteralExpression tuple }:
                    return tuple.Elements.Aggregate(state, (kept, element) => Keep(kept, element, "a tuple", partOnly: false));

                case { Kind: FlowStepKind.Evaluate, Node: AnonymousObjectExpression anonymous }:
                    return anonymous.Values.Aggregate(state, (kept, value) => Keep(kept, value, "an anonymous object", partOnly: false));

                case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression invocation }:
                    state = MoveArguments(state, invocation.Arguments, invocation.Parameters);

                    if(Receiver(invocation) is { } receiver && Tracked(receiver.Local)
                        && (invocation.Method?.DeclaredThisTier == ThisTier.Owned || IsDrop(invocation)))
                    {
                        return state with { MovedBindings = state.MovedBindings.SetItem(receiver.Local, new Moved(invocation.Node, IsDrop(invocation))) };
                    }

                    return state;

                case { Kind: FlowStepKind.Evaluate, Node: NewExpression creation }:
                    state = creation.Initializer.Where(entry => !entry.IsTargetFailed).Aggregate(state, (kept, entry) => Keep(kept, entry.Value, $"'{entry.MemberName}'", partOnly: false));
                    return MoveArguments(state, creation.Arguments, creation.Constructor?.ParametersList);

                case { Kind: FlowStepKind.Evaluate, Node: ArrayLiteralExpression literal }:
                    foreach(var element in literal.Elements)
                    {
                        if(element is LocalReferenceExpression reference && Tracked(reference.Local))
                        {
                            state = Move(state, reference.Local, element);
                        }
                    }

                    return state;

                default:
                    return state;
            }
        }

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

                foreach(var (local, move) in state.MovedBindings)
                {
                    if(move.Node is Expression { Node: { } at } expression && inside.Contains(expression)
                        && !(declaringBlocks.TryGetValue(local, out var home) && inside.Contains(home)))
                    {
                        context.Report(ProgramAnalysisDiagnostics.LoopMove, at, [expression], local.Name);
                    }
                }
            }
        }

        private void Judge(FlowGraph graph, State state, FlowStep step)
        {
            switch(step)
            {
                case { Kind: FlowStepKind.Evaluate, Node: LocalReferenceExpression reference }:
                    if(state.MovedBindings.TryGetValue(reference.Local, out var move))
                    {
                        if(!ReferenceEquals(move.Node, reference) && !IsReceiverOf(move.Node, reference))
                        {
                            context.Report(ProgramAnalysisDiagnostics.UseAfterMove, reference.Node, [reference], reference.Local.Name, move.Dropped ? "dropped" : "moved");
                        }
                    }
                    else if(Has(state, reference.Local, Lease.Borrowed) && !state.Complained.Contains(reference.Local))
                    {
                        context.Report(ProgramAnalysisDiagnostics.LeaseViolation, reference.Node, [reference], reference.Local.Name, "access", "borrowed");
                    }

                    break;

                case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: LocalReferenceExpression source } declaration }:
                    if(LeaseOf(declaration.Local) is Lease.Borrowed && Has(state, source.Local, Lease.Frozen))
                    {
                        context.Report(ProgramAnalysisDiagnostics.LeaseViolation, source.Node, [source], source.Local.Name, "borrow", "frozen");
                    }
                    else if(LeaseOf(declaration.Local) is null && Tracked(source.Local))
                    {
                        JudgeMove(state, source);
                        JudgeShared(state, source, $"owned binding '{declaration.Local.Name}'", declaration.Local.Ownership == OwnershipKind.Owned);
                    }

                    break;

                case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false } assignment }:
                    if(assignment.Target is MemberAccessExpression { Receiver: LocalReferenceExpression holder } && Has(state, holder.Local, Lease.Frozen))
                    {
                        context.Report(ProgramAnalysisDiagnostics.LeaseViolation, holder.Node, [holder], holder.Local.Name, "mutate", "frozen");
                    }

                    if(assignment.Target is LocalReferenceExpression written && assignment.Value is LocalReferenceExpression moved && Tracked(moved.Local))
                    {
                        JudgeMove(state, moved);
                        JudgeShared(state, moved, $"owned binding '{written.Local.Name}'", written.Local.Ownership == OwnershipKind.Owned);
                    }
                    else if(OwnedSlot(assignment.Target) is { } slot && assignment.Value is LocalReferenceExpression stored && Tracked(stored.Local))
                    {
                        JudgeMove(state, stored);
                        JudgeShared(state, stored, slot, owned: true);
                    }

                    break;

                case { Kind: FlowStepKind.Evaluate, Node: ReturnStatement { Value: LocalReferenceExpression returned } }
                    when owning is not null && state.Shared.TryGetValue(returned.Local, out var kept):
                    context.Report(ProgramAnalysisDiagnostics.OwnedReturnNotOwned, returned.Node, [returned], owning.Name, kept.Reason);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression invocation }:
                    JudgeArguments(state, invocation.Arguments, invocation.Parameters);

                    if(Receiver(invocation) is { } receiver)
                    {
                        if(invocation.Method is { } method && tiers.Required(method) >= ThisTier.Borrowed && Has(state, receiver.Local, Lease.Frozen))
                        {
                            context.Report(ProgramAnalysisDiagnostics.LeaseViolation, receiver.Node, [receiver], receiver.Local.Name, "mutate", "frozen");
                        }
                        else if(Tracked(receiver.Local) && invocation.Method?.DeclaredThisTier == ThisTier.Owned)
                        {
                            JudgeMove(state, receiver);
                            JudgeShared(state, receiver, $"the receiver of '{invocation.Method.Name}', which it takes owned", owned: true);
                        }
                    }

                    break;

                case { Kind: FlowStepKind.Evaluate, Node: NewExpression creation }:
                    JudgeArguments(state, creation.Arguments, creation.Constructor?.ParametersList);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: LambdaExpression or AsyncBlockExpression or LocalFunctionStatement } when graph.Nested.TryGetValue(step.Node, out var nested):
                    new Ownership(context, tiers, declaringBlocks, owning: null).Judge(nested, state);
                    break;
            }
        }

        private void JudgeArguments(State state, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
        {
            for(var i = 0; i < arguments.Count && parameters is not null && i < parameters.Count; i++)
            {
                if(parameters[i].Modifier.Ownership == OwnershipKind.Owned
                    && arguments[i] is LocalReferenceExpression argument && Tracked(argument.Local))
                {
                    JudgeMove(state, argument);
                    JudgeShared(state, argument, $"owned parameter '{parameters[i].Name}'", owned: true);
                }
            }
        }

        private void JudgeShared(State state, LocalReferenceExpression source, string destination, bool owned)
        {
            if(owned && state.Shared.TryGetValue(source.Local, out var kept))
            {
                context.Report(ProgramAnalysisDiagnostics.OwnedSourceNotOwned, source.Node, [source], destination, kept.Reason);
            }
        }

        private static State Stored(State state, Expression target, Expression value)
        {
            if(target is LocalReferenceExpression)
            {
                return state;
            }

            if(target is MemberAccessExpression { Receiver: LocalReferenceExpression { Local: var holder }, Member: FieldResult { Field.Name: var restored } }
                && state.Shared.TryGetValue(holder, out var partial) && partial.Field == restored)
            {
                state = state with { Shared = state.Shared.Remove(holder) };
            }

            if(OwnedSlot(target) is not null && value is LocalReferenceExpression moved && Tracked(moved.Local))
            {
                return Move(state, moved.Local, value);
            }

            return Keep(state, value, Path(target) is { } path ? $"'{path}'" : "a field", partOnly: false);
        }

        private static State Keep(State state, Expression value, string where, bool partOnly)
        {
            return Unwrapped(value) switch
            {
                LocalReferenceExpression { Local: { Ownership: OwnershipKind.Owned } local } when !partOnly && Tracked(local) =>
                    state with { Shared = state.Shared.SetItem(local, new Kept($"'{local.Name}' is also held by {where}, where it was stored without a move", null)) },
                MemberAccessExpression { Receiver: LocalReferenceExpression { Local: { Ownership: OwnershipKind.Owned } holder }, Member: FieldResult { Field: var field } }
                    when Tracked(holder) && ThisTiers.IsOwned(field) && !Origins.IsUnchangeable(field.Type) =>
                    state with { Shared = state.Shared.SetItem(holder, new Kept($"its owned field '{field.Name}' is also held by {where}, which read it out without a move", field.Name)) },
                _ => state,
            };
        }

        private static string? OwnedSlot(Expression target)
        {
            return target switch
            {
                FieldReferenceExpression { Field: var field } when ThisTiers.IsOwned(field) => $"owned field '{field.Name}'",
                MemberAccessExpression { Member: FieldResult { Field: var field } } when ThisTiers.IsOwned(field) => $"owned field '{field.Name}'",
                MemberAccessExpression { Member: PropertyResult { Property: { Modifiers: { IsOwned: true, IsStatic: false } } property } } => $"owned property '{property.Name}'",
                PropertyReferenceExpression { Property: { Modifiers: { IsOwned: true, IsStatic: false } } property } => $"owned property '{property.Name}'",
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

        private void JudgeMove(State state, LocalReferenceExpression source)
        {
            if(Has(state, source.Local, Lease.Frozen))
            {
                context.Report(ProgramAnalysisDiagnostics.LeaseViolation, source.Node, [source], source.Local.Name, "move", "frozen");
            }
            else if(Has(state, source.Local, Lease.ReadonlyRef))
            {
                context.Report(ProgramAnalysisDiagnostics.LeaseViolation, source.Node, [source], source.Local.Name, "move", "readonly ref");
            }
        }

        private static State MoveArguments(State state, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
        {
            for(var i = 0; i < arguments.Count && parameters is not null && i < parameters.Count; i++)
            {
                if(parameters[i].Modifier.Ownership == OwnershipKind.Owned
                    && arguments[i] is LocalReferenceExpression argument && Tracked(argument.Local))
                {
                    state = Move(state, argument.Local, arguments[i]);
                }
            }

            return state;
        }

        private static State Move(State state, LocalVariable local, Expression at)
        {
            return state with { MovedBindings = state.MovedBindings.SetItem(local, new Moved(at, Dropped: false)) };
        }

        private static bool Has(State state, LocalVariable lender, Lease kind)
        {
            return state.Leases.Values.Any(lease => ReferenceEquals(lease.Lender, lender) && lease.Kind == kind);
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

        private static LocalReferenceExpression? Receiver(InvocationExpression invocation)
        {
            return ThisTiers.ReceiverOf(invocation) as LocalReferenceExpression;
        }

        private static bool IsReceiverOf(object move, LocalReferenceExpression reference)
        {
            return move is InvocationExpression invocation && ReferenceEquals(Receiver(invocation), reference);
        }

        private static bool IsDrop(InvocationExpression invocation)
        {
            return invocation.Method is { Name: "Drop", ParametersList.Count: 0 }
                && Receiver(invocation) is { } receiver
                && Substructural(receiver.Local.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine;
        }
    }
}