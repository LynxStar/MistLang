// Backward-compatible extension methods for ArgumentListNode.
// The grammar changed from:
//   ArgumentList = Expression (COMMA Expression)*
// to:
//   ArgumentList = Argument (COMMA Argument)*
//   Argument = REF expression:Expression as RefArgument | expression:Expression as ValueArgument
//
// These extensions provide the old Expression/Expressions accessors so existing
// code can work with minimal changes until it is updated to handle ref arguments.

using System.Collections.Immutable;
using Storm.Runtime;

namespace Mist.Generated;

/// <summary>
/// Extension methods on <see cref="ArgumentListNode"/> for backward compatibility
/// with the old grammar structure where ArgumentList directly contained Expression nodes.
/// </summary>
public static class ArgumentListNodeExtensions
{
    /// <summary>
    /// Gets the first argument's expression, unwrapping the <see cref="ArgumentNode"/> wrapper.
    /// </summary>
    public static ExpressionNode GetExpression(this ArgumentListNode node)
    {
        return ExtractExpression(node.Argument);
    }

    /// <summary>
    /// Gets the remaining arguments' expressions, unwrapping each <see cref="ArgumentNode"/> wrapper.
    /// </summary>
    public static ImmutableArray<ExpressionNode> GetExpressions(this ArgumentListNode node)
    {
        var builder = ImmutableArray.CreateBuilder<ExpressionNode>(node.Arguments.Length);
        foreach(var arg in node.Arguments)
        {
            builder.Add(ExtractExpression(arg));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Gets all argument expressions (first + rest) as a flat list.
    /// </summary>
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

    /// <summary>
    /// Extracts the <see cref="ExpressionNode"/> from an <see cref="ArgumentNode"/>,
    /// regardless of whether it is a <see cref="RefArgumentArgumentNode"/> or
    /// <see cref="ValueArgumentArgumentNode"/>.
    /// </summary>
    public static ExpressionNode ExtractExpression(ArgumentNode arg)
    {
        return arg switch
        {
            RefArgumentArgumentNode refArg => refArg.Expression,
            ValueArgumentArgumentNode valueArg => valueArg.Expression,
            _ => throw new System.InvalidOperationException(
                $"Unknown ArgumentNode variant: {arg.GetType().Name}"),
        };
    }
}