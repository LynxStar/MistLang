using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves AST expression nodes into the model's <see cref="Expression"/> hierarchy.
/// All methods are static; shared dependencies are accessed via <see cref="BuilderContext"/>.
///
/// <para>The precedence chain is descended only through nodes where no operator is
/// applied, reaching a bare primary. Any operator, postfix suffix, cast, assignment,
/// or lambda resolves to an <see cref="UnresolvedExpression"/> (deferred to a later
/// slice) that retains the node where resolution stopped.</para>
/// </summary>
internal static class ExpressionBinder
{
    public static Expression BindExpression(
        ExpressionNode expression, BindingContext ctx, BuilderContext context)
    {
        var assignment = expression.AsAssignmentOrNull();
        if(assignment is null)
        {
            return new UnresolvedExpression(expression);
        }

        if(assignment.Operator is not null)
        {
            return new UnresolvedExpression(expression);
        }

        return BindVoidCoalescing(assignment.Target, ctx, context);
    }

    private static Expression BindVoidCoalescing(
        VoidCoalescingNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.LogicalOrs, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindLogicalOr(node.Left, ctx, context);
    }

    private static Expression BindLogicalOr(
        LogicalOrNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.LogicalAnds, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindLogicalAnd(node.Left, ctx, context);
    }

    private static Expression BindLogicalAnd(
        LogicalAndNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.Equalitys, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindEquality(node.Left, ctx, context);
    }

    private static Expression BindEquality(
        EqualityNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.Relationals, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindRelational(node.Left, ctx, context);
    }

    private static Expression BindRelational(
        RelationalNode node, BindingContext ctx, BuilderContext context)
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

    private static Expression BindAdditive(
        AdditiveNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.Multiplicatives, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindMultiplicative(node.Left, ctx, context);
    }

    private static Expression BindMultiplicative(
        MultiplicativeNode node, BindingContext ctx, BuilderContext context)
    {
        if(!IsBare(node.Unarys, node.Left, node.Right))
        {
            return new UnresolvedExpression(node);
        }

        return BindUnary(node.Left, ctx, context);
    }

    private static Expression BindUnary(
        UnaryNode node, BindingContext ctx, BuilderContext context)
    {
        if(node is not PostfixExpressionUnaryNode postfixExpression)
        {
            return new UnresolvedExpression(node);
        }

        return BindPostfix(postfixExpression.Primary, ctx, context);
    }

    private static Expression BindPostfix(
        PostfixNode node, BindingContext ctx, BuilderContext context)
    {
        if(!node.Suffix.IsDefaultOrEmpty)
        {
            return new UnresolvedExpression(node);
        }

        return BindPrimary(node.Target, ctx, context);
    }

    private static Expression BindPrimary(PrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return node switch
        {
            IntegerLiteralExpressionPrimaryNode integer => BindIntegerLiteral(integer, context),
            IdentifierExpressionPrimaryNode identifier => BindIdentifier(identifier, ctx),
            _ => new UnresolvedExpression(node),
        };
    }

    private static Expression BindIntegerLiteral(IntegerLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        var (value, wellKnown) = ParseIntegerLiteral(node.IntegerLiteral.Lexeme);
        var type = context.TypeResolver.LookupWellKnown(wellKnown);
        return new LiteralExpression(node, value, type);
    }

    private static Expression BindIdentifier(IdentifierExpressionPrimaryNode node, BindingContext ctx)
    {
        var local = ctx.Scope.LookupLocal(node.Name.Lexeme);
        if(local is not null)
        {
            return new LocalReferenceExpression(node, local);
        }

        return new UnresolvedExpression(node);
    }

    /// <summary>
    /// Determines whether a precedence-chain node has no operator applied.
    /// </summary>
    private static bool IsBare<T>(ImmutableArray<T> repeatedOperands, T left, T? right)
        where T : class
    {
        return repeatedOperands.IsDefaultOrEmpty && (right is null || ReferenceEquals(right, left));
    }

    /// <summary>
    /// Parses a decimal integer literal lexeme into its value and the well-known type
    /// it maps to, honouring the u/l suffixes.
    /// </summary>
    private static (object Value, WellKnownType Type) ParseIntegerLiteral(string lexeme)
    {
        var upper = lexeme.ToUpperInvariant();
        var wellKnown = upper switch
        {
            _ when upper.EndsWith("UL", StringComparison.Ordinal) || upper.EndsWith("LU", StringComparison.Ordinal) => WellKnownTypes.UInt64,
            _ when upper.EndsWith("L", StringComparison.Ordinal) => WellKnownTypes.Int64,
            _ when upper.EndsWith("U", StringComparison.Ordinal) => WellKnownTypes.UInt32,
            _ => WellKnownTypes.Int32,
        };

        var digits = lexeme.TrimEnd('u', 'U', 'l', 'L');
        var value = long.TryParse(digits, out var parsed) ? parsed : 0L;
        return (value, wellKnown);
    }
}