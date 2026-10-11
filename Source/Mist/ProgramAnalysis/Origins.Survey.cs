using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

internal sealed partial class Origins
{
    private void Survey(IReadOnlyList<object> inside)
    {
        var values = new Dictionary<LocalVariable, List<Expression>>(ReferenceEqualityComparer.Instance);
        var targets = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach(var node in inside)
        {
            switch(node)
            {
                case LocalDeclarationStatement declaration:
                    values[declaration.Local] = declaration.Initializer is { } initializer ? [initializer] : [];

                    if(declaration is { Local: { Ownership: OwnershipKind.Frozen, Parameter: null }, Initializer: { } lender })
                    {
                        leased[declaration.Local] = lender;
                    }

                    if(declaration.Local.IsRef && declaration.Initializer is { } referent)
                    {
                        refTargets[declaration.Local] = referent;
                        MarkAliased(referent);
                    }

                    break;

                case WithBlockStatement { Binding: { } binding } ambient:
                    values[binding] = [ambient.Value];
                    break;

                case AssignmentExpression assignment:
                    foreach(var (target, value) in Deconstructions.Pairs(assignment))
                    {
                        Assign(target, value, values);
                    }

                    if(assignment.Target is TupleLiteralExpression written)
                    {
                        targets.UnionWith(BodyWalker.DescendantsAndSelf(written));
                    }

                    break;

                case InvocationExpression invocation:
                    MarkByRef(invocation.Arguments, invocation.Parameters);
                    MarkHanded(invocation.Arguments, invocation.Parameters);
                    if(invocation.Method is not { Modifiers: { IsReadonly: true } or { IsPure: true } })
                    {
                        MarkStructAssigned(ThisTiers.ReceiverOf(invocation));
                    }

                    if(invocation.Method is { Modifiers.IsStatic: false } called && ThisTiers.ReceiverOf(invocation) is { } receiver && ThisEscapes.Leaks(called))
                    {
                        MarkEscaped(receiver);
                    }

                    break;

                case NewExpression allocation:
                    MarkByRef(allocation.Arguments, allocation.Constructor?.ParametersList);
                    MarkHanded(allocation.Arguments, allocation.Constructor?.ParametersList);
                    break;

                case ObjectInitializerAssignment { IsTargetFailed: false } entry:
                    MarkEscaped(entry.Value);
                    break;

                case TupleLiteralExpression tuple when !targets.Contains(tuple):
                    MarkEscaped(tuple.Elements);
                    break;

                case ArrayLiteralExpression array:
                    MarkEscaped(array.Elements);
                    break;

                case AnonymousObjectExpression anonymous:
                    MarkEscaped(anonymous.Values);
                    break;

                case LambdaExpression lambda:
                    MarkCaptured(lambda.Parameters, Captures.Inside(lambda));
                    break;

                case LocalFunctionStatement function:
                    functions[function.Function] = function;
                    MarkCaptured(function.Parameters, Captures.Inside(function));
                    break;

                case IsPatternExpression test:
                    BindPattern(test.Pattern, test.Scrutinee, []);
                    break;

                case SwitchExpression chosen:
                    foreach(var arm in chosen.Arms)
                    {
                        BindPattern(arm.Pattern, chosen.Scrutinee, []);
                    }

                    break;
            }
        }

        foreach(var (local, _) in values)
        {
            if(!aliased.Contains(local) && !local.IsRef)
            {
                _ = fresh.Add(local);
            }
        }

        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var local in fresh.ToList())
            {
                if(values[local].Exists(value => Of(value) != Origin.Own))
                {
                    _ = fresh.Remove(local);
                    changed = true;
                }
            }
        }

        foreach(var (local, list) in values)
        {
            if(!assigned.Contains(local) && !aliased.Contains(local) && list is [var only] && Unwrapped(only) is LambdaExpression lambda)
            {
                heldLambdas[local] = lambda;
            }
        }
    }

    private void Assign(Expression target, Expression value, Dictionary<LocalVariable, List<Expression>> values)
    {
        if(target is not LocalReferenceExpression local)
        {
            MarkStructAssigned(ReceiverOf(target));
            MarkEscaped(value);
            return;
        }

        _ = assigned.Add(local.Local);

        if(values.TryGetValue(local.Local, out var list))
        {
            list.Add(value);
        }
    }

    private void BindPattern(Pattern? pattern, Expression scrutinee, IReadOnlyList<Hop> hops)
    {
        if(pattern?.Binding is { } binding)
        {
            var level = hops.Count == 0 ? scrutinee.Type : hops[^1].Field.Type;
            bound[binding] = new Bound(scrutinee, hops, IsOwnCopy(binding.Type) && !ThisTiers.IsValue(level), Narrows(level, binding.Type));
        }

        switch(pattern)
        {
            case PropertyPattern property:
                BindPattern(property.TypeTest, scrutinee, hops);

                foreach(var subpattern in property.Subpatterns)
                {
                    if(subpattern.Member is FieldResult { Field: var field })
                    {
                        BindPattern(subpattern.Pattern, scrutinee, [.. hops, new Hop(field, property.NarrowedType)]);
                    }
                }

                break;

            case ConjunctionPattern conjunction:
                foreach(var operand in conjunction.Operands)
                {
                    BindPattern(operand, scrutinee, hops);
                }

                break;

            case DisjunctionPattern disjunction:
                foreach(var operand in disjunction.Operands)
                {
                    BindPattern(operand, scrutinee, hops);
                }

                break;

            case NegatedPattern negated:
                BindPattern(negated.Operand, scrutinee, hops);
                break;

            case TypeTestPattern { Refinement: { } refinement }:
                BindPattern(refinement, scrutinee, hops);
                break;
        }
    }

    private static Expression Unwrapped(Expression expression)
    {
        return expression is ImplicitConversionExpression { Operator: null } conversion ? Unwrapped(conversion.Operand) : expression;
    }

    private void MarkByRef(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        if(parameters is null)
        {
            return;
        }

        for(var i = 0; i < arguments.Count && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.IsRef)
            {
                MarkAliased(arguments[i]);
            }
        }
    }

    private void MarkAliased(Expression argument)
    {
        if(argument is LocalReferenceExpression reference)
        {
            _ = aliased.Add(reference.Local);
        }
        else if(StructRoot(argument) is { } root)
        {
            _ = aliased.Add(root);
        }
    }

    private void MarkHanded(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(parameters is null || i >= parameters.Count || parameters[i].Modifier.Ownership != OwnershipKind.Owned)
            {
                MarkEscaped(arguments[i]);
            }
        }
    }

    private void MarkEscaped(IEnumerable<Expression> values)
    {
        foreach(var value in values)
        {
            MarkEscaped(value);
        }
    }

    private void MarkEscaped(Expression value)
    {
        if(Stripped(value) is LocalReferenceExpression reference)
        {
            _ = escaped.Add(reference.Local);
        }
    }

    private void MarkCaptured(IEnumerable<LocalVariable> parameters, IReadOnlyList<object> inside)
    {
        var names = Captures.OwnNames(parameters, inside);

        foreach(var reference in inside.OfType<LocalReferenceExpression>())
        {
            if(!names.Contains(reference.Local))
            {
                _ = escaped.Add(reference.Local);
            }
        }
    }

    private static Expression Stripped(Expression expression)
    {
        return expression switch
        {
            ImplicitConversionExpression { Operator: null } conversion => Stripped(conversion.Operand),
            CastExpression { Operator: null } cast => Stripped(cast.Operand),
            AsExpression converted => Stripped(converted.Operand),
            _ => expression,
        };
    }

    private void MarkStructAssigned(Expression? receiver)
    {
        if(receiver is not null && StructRoot(receiver) is { } root)
        {
            _ = assigned.Add(root);
        }
    }

    private static LocalVariable? StructRoot(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression reference when ThisTiers.IsValue(reference.Local.Type) => reference.Local,
            MemberAccessExpression { Member: FieldResult } access when ThisTiers.IsValue(access.Receiver.Type) => StructRoot(access.Receiver),
            IndexExpression { Indexer: null } index when ThisTiers.IsValue(index.Receiver.Type) => StructRoot(index.Receiver),
            _ => null,
        };
    }

    private static Expression? ReceiverOf(Expression target)
    {
        return target switch
        {
            MemberAccessExpression access => access.Receiver,
            IndexExpression index => index.Receiver,
            ExtensionPropertyAccessExpression extension => extension.Receiver,
            _ => null,
        };
    }

    private sealed record Bound(Expression Scrutinee, IReadOnlyList<Hop> Hops, bool Unboxed, bool Narrowed);

    private readonly record struct Hop(Field Field, IMistType HolderType);
}