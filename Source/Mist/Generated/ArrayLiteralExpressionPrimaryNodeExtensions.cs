using System.Collections.Immutable;

namespace Mist.Generated;

public static class ArrayLiteralExpressionPrimaryNodeExtensions
{
    /// <summary>
    /// Every element in source order; the empty literal <c>[]</c> has no element list and yields none.
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

    public static bool IsEmptyLiteral(this ArrayLiteralExpressionPrimaryNode node)
    {
        return node.Elements is null;
    }
}