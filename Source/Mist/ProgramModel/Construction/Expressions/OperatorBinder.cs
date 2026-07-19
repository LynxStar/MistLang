using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Expressions;

/// <summary>
/// Walks the precedence chain from VoidCoalescing down to Unary.
/// Each level checks whether an operator is actually applied; if not, it passes
/// through to the next level. When operators are present, they resolve to
/// <see cref="UnresolvedExpression"/> until operator binding is implemented.
/// </summary>
internal static class OperatorBinder
{
    internal static Expression BindVoidCoalescing(VoidCoalescingNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.LogicalOrs, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindLogicalOr(node.Left, ctx, context);
    }

    private static Expression BindLogicalOr(LogicalOrNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.LogicalAnds, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindLogicalAnd(node.Left, ctx, context);
    }

    private static Expression BindLogicalAnd(LogicalAndNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.Equalitys, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindEquality(node.Left, ctx, context);
    }

    private static Expression BindEquality(EqualityNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.Relationals, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindRelational(node.Left, ctx, context);
    }

    private static Expression BindRelational(RelationalNode node, BindingContext ctx, BuilderContext context)
    {
        if(node is not RelationalComparisonRelationalNode comparison)
        {
            return new UnresolvedExpression(node);
        }

        if(!IsBare(comparison.Additives, comparison.Left, comparison.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindAdditive(comparison.Left, ctx, context);
    }

    private static Expression BindAdditive(AdditiveNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.Multiplicatives, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindMultiplicative(node.Left, ctx, context);
    }

    private static Expression BindMultiplicative(MultiplicativeNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.Unarys, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindUnary(node.Left, ctx, context);
    }

    private static Expression BindUnary(UnaryNode node, BindingContext ctx, BuilderContext context)
    {
        if(node is not PostfixExpressionUnaryNode postfixExpression)
        {
            return new UnresolvedExpression(node);
        }

        return PostfixBinder.BindPostfix(postfixExpression.Primary, ctx, context);
    }

    /// <summary>
    /// Determines whether a precedence-chain node has no operator applied.
    /// </summary>
    private static bool IsBare<T>(ImmutableArray<T> repeatedOperands, T left, T? right)
        where T : class
    {
        return repeatedOperands.IsDefaultOrEmpty && (right is null || ReferenceEquals(right, left));
    }
}