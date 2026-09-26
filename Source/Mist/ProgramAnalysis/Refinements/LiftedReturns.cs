using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// What a member produces, read off its own returns rather than its declared type, as refinement-types
/// Requirement 18 lifts it: `int Count { get { return 9; } }` produces `int 9` wherever it is read. Beside
/// <see cref="ReadonlyInference"/> and for its reason -- a member proves its own signature, and a use asks
/// the signature and never the body -- so a return that reads another member reads that member's lift,
/// making this a fixed point over the call graph. Only a constant or a refined value lifts; what the flow
/// narrowed does not, so the lift is exactly what the returned expressions' types already say.
/// </summary>
internal sealed class LiftedReturns
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
            : FlowGraphBuilder.Build(root).Blocks
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
