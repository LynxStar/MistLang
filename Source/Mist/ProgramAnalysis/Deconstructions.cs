using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis;

/// <summary>
/// What an assignment writes, one target at a time: the assignment itself, or, for a deconstruction,
/// each element of its tuple target paired with the element of the value it takes -- the literal's own
/// element when the value is a tuple literal, and otherwise an element read off the value, so every rule
/// that reads an assignment sees each write as though it were written alone.
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