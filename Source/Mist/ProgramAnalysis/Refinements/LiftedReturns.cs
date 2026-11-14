using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// Refinement-types Requirement 18 lifts constant and refined returns, including other members' lifts,
/// into a member's inferred signature. As with <see cref="ReadonlyInference"/>, uses read that signature;
/// flow narrowing contributes nothing beyond the returned expressions' types.
/// </summary>
internal sealed class LiftedReturns(Func<object, FlowGraph>? graphOf = null)
{
    private readonly Dictionary<object, Admitted?> settled = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> settling = new(ReferenceEqualityComparer.Instance);

    public Admitted? Of(Expression value)
    {
        return value switch
        {
            InvocationExpression { Method: { Modifiers.IsAsync: false } method } => Lift(method, method.Body, method.ReturnType),
            MemberAccessExpression { Member: PropertyResult { Property: var property } } => Of(property),
            PropertyReferenceExpression { Property: var property } => Of(property),
            IndexExpression { Indexer: { } indexer } => Lift(indexer, Root(indexer.Getter), indexer.Type),
            _ => null,
        };
    }

    public Admitted? Of(Property property)
    {
        return Lift(property, Root(property.Getter), property.Type);
    }

    public Admitted? Of(Method method)
    {
        return method.Modifiers.IsAsync ? null : Lift(method, method.Body, method.ReturnType);
    }

    private static object? Root(PropertyAccessor? getter)
    {
        return getter is { IsAuto: false } ? (object?)getter.Body ?? getter.ExpressionBody : null;
    }

    private Admitted? Lift(object member, object? root, IMistType declared)
    {
        var unrefined = Requirement.Unrefined(declared);

        if(root is null || !(Requirement.IsIntegral(unrefined) || unrefined.FullyQualifiedName == WellKnownTypes.Boolean.FullyQualifiedName))
        {
            return null;
        }

        if(settled.TryGetValue(member, out var known))
        {
            return known;
        }

        if(!settling.Add(member))
        {
            return null;
        }

        var lifted = Returns(root, declared);
        _ = settling.Remove(member);
        settled[member] = lifted;
        return lifted;
    }

    private Admitted? Returns(object root, IMistType declared)
    {
        var values = root is Expression body
            ? [body]
            : (graphOf ?? FlowGraphs.Shared.GraphOf)(root).Blocks
                .SelectMany(block => block.Steps)
                .Select(step => step.Node is ReturnStatement { Value: { } value } ? value : null)
                .OfType<Expression>()
                .ToList();

        if(values.Count == 0)
        {
            return null;
        }

        var lifted = Admitted.Nothing;

        foreach(var value in values)
        {
            if(Produced(value) is not { } admitted)
            {
                return null;
            }

            lifted = lifted.Or(admitted);
        }

        lifted = lifted.And(Requirement.OfType(declared)?.Values ?? Admitted.Everything);
        return lifted.IsEverything ? null : lifted;
    }

    public Admitted? Produced(Expression value)
    {
        if(Requirement.Constant(value) is { } constant)
        {
            return Admitted.Exactly(constant);
        }

        if(value is ImplicitConversionExpression { Operator: null } conversion && Requirement.IsIntegral(Requirement.Unrefined(conversion.Type)))
        {
            return Produced(conversion.Operand);
        }

        var typed = Requirement.ValuesOf(value.Type);
        var lifted = Of(value);

        return typed is null && lifted is null ? null : (typed ?? Admitted.Everything).And(lifted ?? Admitted.Everything);
    }
}