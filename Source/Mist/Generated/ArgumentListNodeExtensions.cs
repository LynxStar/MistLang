using System.Collections.Immutable;

namespace Mist.Generated;

/// <summary>
/// Each <see cref="ArgumentNode"/> wraps its expression in a ref or value form; these unwrap it for call sites
/// that read only the expression. <see cref="GetExpression"/> reads the first argument and
/// <see cref="GetExpressions"/> the rest.
/// </summary>
public static class ArgumentListNodeExtensions
{
    public static ExpressionNode GetExpression(this ArgumentListNode node)
    {
        return ExtractExpression(node.Argument);
    }

    public static ImmutableArray<ExpressionNode> GetExpressions(this ArgumentListNode node)
    {
        var builder = ImmutableArray.CreateBuilder<ExpressionNode>(node.Arguments.Length);
        foreach(var arg in node.Arguments)
        {
            builder.Add(ExtractExpression(arg));
        }

        return builder.MoveToImmutable();
    }

    public static ImmutableArray<ExpressionNode> GetAllExpressions(this ArgumentListNode node)
    {
        var builder = ImmutableArray.CreateBuilder<ExpressionNode>(1 + node.Arguments.Length);
        builder.Add(ExtractExpression(node.Argument));
        foreach(var arg in node.Arguments)
        {
            builder.Add(ExtractExpression(arg));
        }

        return builder.MoveToImmutable();
    }

    public static ExpressionNode ExtractExpression(ArgumentNode arg)
    {
        return arg switch
        {
            RefArgumentArgumentNode refArg => refArg.Expression,
            ValueArgumentArgumentNode valueArg => valueArg.Expression,
            _ => throw new InvalidOperationException(
                $"Unknown ArgumentNode variant: {arg.GetType().Name}"),
        };
    }
}