using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Expose deconstructions as individual writes so assignment rules also cover tuple targets.
/// Nonliteral values need synthetic element reads.
/// </summary>
internal static class Deconstructions
{
    public static IEnumerable<(Expression Target, Expression Value)> Pairs(AssignmentExpression assignment)
    {
        return Pairs(assignment.Target, assignment.Value);
    }

    private static IEnumerable<(Expression Target, Expression Value)> Pairs(Expression target, Expression value)
    {
        if(target is not TupleLiteralExpression tuple)
        {
            yield return (target, value);
            yield break;
        }

        var literal = Unwrapped(value) as TupleLiteralExpression;

        for(var i = 0; i < tuple.Elements.Count; i++)
        {
            var element = literal is not null && literal.Elements.Count == tuple.Elements.Count
                ? literal.Elements[i]
                : new MemberAccessExpression(value.Node, value, $"Item{i + 1}", ElementType(value.Type, i) ?? tuple.Elements[i].Type);

            foreach(var pair in Pairs(tuple.Elements[i], element))
            {
                yield return pair;
            }
        }
    }

    private static IMistType? ElementType(IMistType type, int index)
    {
        return type switch
        {
            INamedTupleType named => ElementType(named.Underlying, index),
            ITupleType tuple when index < tuple.ElementTypes.Count => tuple.ElementTypes[index],
            _ => null,
        };
    }

    private static Expression Unwrapped(Expression expression)
    {
        return expression is ImplicitConversionExpression { Operator: null } conversion ? Unwrapped(conversion.Operand) : expression;
    }
}