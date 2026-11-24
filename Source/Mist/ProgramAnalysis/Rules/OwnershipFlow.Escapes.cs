using Mist.Common.Diagnostics;
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
/// A lent reference is non-storable (ownership-system Requirements 4.4, 5.2, 6.2 and 11): what holds a
/// borrowed, frozen or readonly ref binding's value may reach no field, element, collection, return,
/// unrestricted parameter (a constructor chain's included), closure capture, or local that outlives it,
/// so it is lent on only to a lent tier (Requirements 12.2 to 12.4). A bare
/// <c>ref</c> parameter carries no ownership, so neither its value nor a pass to one is judged here.
/// </summary>
internal sealed partial class OwnershipFlow
{
    private void JudgeEscapes(State state, FlowStep step)
    {
        if(step.Kind != FlowStepKind.Evaluate)
        {
            return;
        }

        switch(step.Node)
        {
            case AssignmentExpression assignment:
                foreach(var (target, value) in Deconstructions.Pairs(assignment))
                {
                    if(StoredIn(target, Aliased) is { } destination)
                    {
                        Escape(state, value, "store", destination);
                    }
                    else if(Written(target) is { } written)
                    {
                        Escape(state, value, "store", lent => Outlives(written, lent));
                    }
                }

                break;

            case InvocationExpression invocation:
                HandOff(state, invocation.Arguments, invocation.Parameters, invocation.Method is { } called ? $"'{called.Name}'" : "a function value");
                break;

            case NewExpression creation:
                HandOff(state, creation.Arguments, creation.Constructor?.ParametersList, $"'{creation.Type.Name}'");
                break;

            case ObjectInitializerAssignment { IsTargetFailed: false, Add: { } add } entry:
                HandOff(state, [entry.Value], add.ParametersList, $"'{add.Name}'");
                break;

            case ObjectInitializerAssignment { IsTargetFailed: false } entry when OwnedProvenance.OwnershipOf(entry.Member) != OwnershipKind.Owned:
                Escape(state, entry.Value, "store", $"in '{entry.MemberName}'");
                break;

            case ReturnStatement { Value: { } value, IsRef: false } returned when Returning(returned) is { } from:
                Escape(state, value, "return", from);
                break;

            case ArrayLiteralExpression literal:
                Escape(state, literal.Elements, "in an array");
                break;

            case AnonymousObjectExpression anonymous:
                Escape(state, anonymous.Values, "in an anonymous object");
                break;

            case TupleLiteralExpression tuple when !Deconstructed(tuple):
                Escape(state, tuple.Elements, "in a tuple");
                break;

            case LambdaExpression or LocalFunctionStatement or AsyncBlockExpression when Captures.Of(step.Node) is var (inner, inside):
                var closure = step.Node switch { LambdaExpression => "in a lambda", LocalFunctionStatement => "in a local function", _ => "in an async block" };
                foreach(var reference in inside.OfType<LocalReferenceExpression>()
                    .Where(reference => !inner.Contains(reference.Local) && EscapeAnalyzer.Why(reference.Local).Kind is null)
                    .DistinctBy(reference => reference.Local))
                {
                    Escape(state, reference, "capture", closure);
                }

                break;
        }
    }

    internal void JudgeChained(Expression argument, Parameter parameter, Constructor chained)
    {
        if(Keeps(parameter.Modifier))
        {
            Escape(Nothing, argument, "pass", $"to unrestricted parameter '{parameter.Name}' of '{chained.DeclaringType?.Name ?? chained.Name}'");
        }
    }

    private void HandOff(State state, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, string callee)
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
                Escape(state, arguments[i], "pass", $"to {callee}");
            }
            else if(parameter is not null && Keeps(parameter.Modifier))
            {
                Escape(state, arguments[i], "pass", $"to unrestricted parameter '{parameter.Name}' of {callee}");
            }
        }
    }

    private void Escape(State state, IEnumerable<Expression> elements, string destination)
    {
        foreach(var element in elements)
        {
            Escape(state, element, "store", destination);
        }
    }

    private void Escape(State state, Expression value, string action, string destination)
    {
        Escape(state, value, action, _ => destination);
    }

    private void Escape(State state, Expression value, string action, Func<LocalVariable, string?> destination)
    {
        foreach(var leaf in OwnershipAnalyzer.Selected(value))
        {
            if(LentIn(state, leaf, destination) is var (lent, tier, reached))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.LentReferenceEscapes, leaf.Node, [leaf],
                    new EnumValue(action), new EnumValue(tier), new NameValue(lent.Name), new NameValue(reached));
            }
        }
    }

    private LocalVariable? Written(Expression target)
    {
        return OwnershipAnalyzer.Unwrapped(target) switch
        {
            LocalReferenceExpression { Local.IsRef: true, Local: var alias } => Aliased(alias) is LocalReferenceExpression { Local.IsRef: false } referent ? referent.Local : null,
            LocalReferenceExpression { Local: var written } => written,
            _ => null,
        };
    }

    private string? Outlives(LocalVariable written, LocalVariable lent)
    {
        if(own is not null && !own.Contains(written))
        {
            return $"in '{written.Name}', declared outside this closure";
        }

        if(locals.IsCaptured(written))
        {
            return $"in '{written.Name}', which a closure captures";
        }

        if(!declaringBlocks.TryGetValue(written, out var outer) || !declaringBlocks.TryGetValue(lent, out var inner) || ReferenceEquals(outer, inner))
        {
            return null;
        }

        for(object node = inner; parents.TryGetValue(node, out var parent); node = parent)
        {
            if(ReferenceEquals(parent, outer))
            {
                return $"in '{written.Name}', declared outside the block that holds '{lent.Name}'";
            }
        }

        return null;
    }

    private (LocalVariable Lent, string Tier, string Destination)? LentIn(State state, Expression leaf, Func<LocalVariable, string?> destination)
    {
        if(ThisTiers.IsValue(leaf.Type) || Origins.IsUnchangeable(leaf.Type))
        {
            return null;
        }

        switch(OwnershipAnalyzer.Unwrapped(leaf))
        {
            case LocalReferenceExpression { Local: var local }:
                foreach(var held in ValuesOf(state.Values, Place.Of(local)).OfType<LocalVariable>().OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    if(LentKind(state, held, []) is { } tier && destination(held) is { } reached)
                    {
                        return (held, tier, reached);
                    }
                }

                return null;

            case MemberAccessExpression { Member: FieldResult { Field: var field }, Receiver: var receiver } when Place.Holds(field):
                return LentIn(state, receiver, destination);

            default:
                return null;
        }
    }

    private string? LentKind(State state, LocalVariable value, HashSet<LocalVariable> seen)
    {
        if(!seen.Add(value) || NonOwningKind(value) is not { } kind || kind == "ref")
        {
            return null;
        }

        return !locals.IsLoan(value) || (state.Leases.TryGetValue(value, out var loans)
            && loans.Any(loan => Tracked(loan.Lender) || LentKind(state, loan.Lender.Root, seen) is not null)) ? kind : null;
    }

    internal static bool Keeps(ParameterModifier modifier)
    {
        return modifier is { Ownership: OwnershipKind.Unrestricted, IsRef: false, Direction: not ByRefDirection.Out };
    }

    internal static string? StoredIn(Expression target, Func<LocalVariable, Expression?> referent)
    {
        return target switch
        {
            LocalReferenceExpression { Local: { Parameter.Modifier: var modifier } parameter } when modifier.IsRef || modifier.Direction == ByRefDirection.Out
                => $"in parameter '{parameter.Name}', which its caller holds",
            LocalReferenceExpression { Local: { IsRef: true } alias } when referent(alias) is { } storage and not LocalReferenceExpression
                => $"in '{alias.Name}', which refers to {(Spelled(storage) is { } path ? $"'{path}'" : "storage outside this scope")}",
            LocalReferenceExpression or TupleLiteralExpression => null,
            IndexExpression index => Spelled(index.Receiver) is { } holder ? $"in an element of '{holder}'" : "in an element",
            _ when ThisTiers.OwnedSlot(target) is not null => null,
            _ => Spelled(target) is { } written ? $"in '{written}'" : "in storage outside this scope",
        };
    }

    internal static string? Spelled(Expression expression)
    {
        return OwnershipAnalyzer.Unwrapped(expression) switch
        {
            MemberAccessExpression { Receiver: TypeReferenceExpression owner } access => $"{owner.ReferencedType.Name}.{access.MemberName}",
            MemberAccessExpression { Receiver: var receiver } access => Spelled(receiver) is { } holder ? $"{holder}.{access.MemberName}" : access.MemberName,
            IndexExpression index => Spelled(index.Receiver) is { } holder ? $"{holder}[]" : null,
            var other => Path(other),
        };
    }

    private string? Returning(ReturnStatement returned)
    {
        object node = returned;

        while(parents.TryGetValue(node, out var parent))
        {
            switch(parent)
            {
                case LocalFunctionStatement { Function: var function }:
                    return OwnershipAnalyzer.ReturnsOwned(function) ? null : $"from '{function.Name}'";
                case LambdaExpression or AsyncBlockExpression:
                    return "from a lambda";
            }

            node = parent;
        }

        return owning is not null ? null : context.MemberOf(returned) is Method { Name: var name } ? $"from '{name}'" : "from this body";
    }

    private bool Deconstructed(TupleLiteralExpression tuple)
    {
        object node = tuple;

        while(parents.TryGetValue(node, out var parent))
        {
            switch(parent)
            {
                case TupleLiteralExpression or ImplicitConversionExpression { Operator: null }:
                    node = parent;
                    continue;
                case AssignmentExpression assignment:
                    return ReferenceEquals(assignment.Target, node) || assignment.Target is TupleLiteralExpression;
                case TupleDeconstructionStatement deconstruction:
                    return ReferenceEquals(deconstruction.Initializer, node);
                default:
                    return false;
            }
        }

        return false;
    }
}