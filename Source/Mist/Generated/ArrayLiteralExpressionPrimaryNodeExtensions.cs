// Backward-compatible extension methods for ArrayLiteralExpressionPrimaryNode.
// The grammar changed from two alternatives:
//   Primary = ... | LEFT_BRACKET RIGHT_BRACKET as EmptyArrayLiteralExpression
//                 | LEFT_BRACKET first:Expression (COMMA rest:Expression)* COMMA? RIGHT_BRACKET as ArrayLiteralExpression
// to one:
//   Primary = ... | LEFT_BRACKET elements:ArrayElementList? RIGHT_BRACKET as ArrayLiteralExpression
//   ArrayElementList = first:Expression (COMMA rest:Expression)* COMMA?
//
// EmptyArrayLiteralExpressionPrimaryNode no longer exists; an empty literal is now the
// same node with a null element list. Callers previously had to assemble the full element
// sequence by hand from First plus Expressions, so this exposes it flattened instead.

using System.Collections.Immutable;

namespace Mist.Generated;

/// <summary>
/// Extension methods on <see cref="ArrayLiteralExpressionPrimaryNode"/> that flatten the
/// optional <see cref="ArrayElementListNode"/> into a single element sequence.
/// </summary>
public static class ArrayLiteralExpressionPrimaryNodeExtensions
{
    /// <summary>
    /// Gets every element expression of the literal in source order, or an empty array for
    /// the empty literal <c>[]</c>. Replaces the old pattern of reading <c>First</c> and
    /// then appending <c>Expressions</c>.
    /// </summary>
    public static ImmutableArray<ExpressionNode> GetElements(this ArrayLiteralExpressionPrimaryNode node)
    {
        if(node.Elements is not { } list)
        {
            return ImmutableArray<ExpressionNode>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ExpressionNode>(list.Expressions.Length + 1);
        builder.Add(list.First);

        foreach(var element in list.Expressions)
        {
            builder.Add(element);
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// True for the empty array literal <c>[]</c>, which carries no element list. This is
    /// the condition that previously selected the separate
    /// <c>EmptyArrayLiteralExpressionPrimaryNode</c> case.
    /// </summary>
    public static bool IsEmptyLiteral(this ArrayLiteralExpressionPrimaryNode node)
    {
        return node.Elements is null;
    }
}