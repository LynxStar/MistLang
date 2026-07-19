using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Expressions;

/// <summary>
/// Walks the precedence chain from VoidCoalescing down to Unary.
/// Each level binds its operator into a <see cref="BinaryExpression"/> when present,
/// or passes through to the next level when bare.
/// </summary>
internal static class OperatorBinder
{
    internal static Expression BindVoidCoalescing(VoidCoalescingNode node, BindingContext ctx, BuilderContext context)
    {
        if(IsBare(node.LogicalOrs, node.Left, node.Right))
        {
            return BindLogicalOr(node.Left, ctx, context);
        }

        return new UnresolvedExpression(node);
    }

    private static Expression BindLogicalOr(LogicalOrNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindLogicalAnd(node.Left, ctx, context);

        if(IsBare(node.LogicalAnds, node.Left, node.Right))
        {
            return left;
        }

        // Left-associative: fold repeated operands.
        foreach(var operand in node.LogicalAnds)
        {
            var right = BindLogicalAnd(operand, ctx, context);
            left = MakeBinary(node, left, BinaryOperatorKind.LogicalOr, right);
        }

        return left;
    }

    private static Expression BindLogicalAnd(LogicalAndNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindEquality(node.Left, ctx, context);

        if(IsBare(node.Equalitys, node.Left, node.Right))
        {
            return left;
        }

        foreach(var operand in node.Equalitys)
        {
            var right = BindEquality(operand, ctx, context);
            left = MakeBinary(node, left, BinaryOperatorKind.LogicalAnd, right);
        }

        return left;
    }

    private static Expression BindEquality(EqualityNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindRelational(node.Left, ctx, context);

        if(IsBare(node.Relationals, node.Left, node.Right))
        {
            return left;
        }

        var op = MapOperator(node.Operator.Lexeme);
        var right = BindRelational(node.Right!, ctx, context);
        return MakeBinary(node, left, op, right);
    }

    private static Expression BindRelational(RelationalNode node, BindingContext ctx, BuilderContext context)
    {
        if(node is not RelationalComparisonRelationalNode comparison)
        {
            return new UnresolvedExpression(node);
        }

        var left = BindAdditive(comparison.Left, ctx, context);

        if(IsBare(comparison.Additives, comparison.Left, comparison.Right))
        {
            return left;
        }

        var op = MapOperator(comparison.Operator.Lexeme);
        var right = BindAdditive(comparison.Right!, ctx, context);
        return MakeBinary(node, left, op, right);
    }

    private static Expression BindAdditive(AdditiveNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindMultiplicative(node.Left, ctx, context);

        if(IsBare(node.Multiplicatives, node.Left, node.Right))
        {
            return left;
        }

        var op = MapOperator(node.Operator.Lexeme);
        var right = BindMultiplicative(node.Right!, ctx, context);
        return MakeBinary(node, left, op, right);
    }

    private static Expression BindMultiplicative(MultiplicativeNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindUnary(node.Left, ctx, context);

        if(IsBare(node.Unarys, node.Left, node.Right))
        {
            return left;
        }

        var op = MapOperator(node.Operator.Lexeme);
        var right = BindUnary(node.Right!, ctx, context);
        return MakeBinary(node, left, op, right);
    }

    private static Expression BindUnary(UnaryNode node, BindingContext ctx, BuilderContext context)
    {
        switch(node)
        {
            case PostfixExpressionUnaryNode postfixExpression:
                return PostfixBinder.BindPostfix(postfixExpression.Primary, ctx, context);

            case NegateUnaryNode negate:
                {
                    var operand = BindUnary(negate.Operand, ctx, context);
                    return new UnaryExpression(node, UnaryOperatorKind.Negate, operand, operand.Type);
                }

            case LogicalNotUnaryNode logicalNot:
                {
                    var operand = BindUnary(logicalNot.Operand, ctx, context);
                    return new UnaryExpression(node, UnaryOperatorKind.LogicalNot, operand, operand.Type);
                }

            default:
                return new UnresolvedExpression(node);
        }
    }

    private static BinaryExpression MakeBinary(Storm.Runtime.SyntaxNode node, Expression left, BinaryOperatorKind op, Expression right)
    {
        var resultType = InferBinaryResultType(left, op, right);
        return new BinaryExpression(node, left, op, right, resultType);
    }

    /// <summary>
    /// Infers the result type of a binary operation. Simplified: comparison/logical
    /// operators produce bool, arithmetic produces the left operand's type.
    /// </summary>
    private static IMistType InferBinaryResultType(Expression left, BinaryOperatorKind op, Expression right)
    {
        return op switch
        {
            BinaryOperatorKind.Equal or
            BinaryOperatorKind.NotEqual or
            BinaryOperatorKind.LessThan or
            BinaryOperatorKind.GreaterThan or
            BinaryOperatorKind.LessThanOrEqual or
            BinaryOperatorKind.GreaterThanOrEqual or
            BinaryOperatorKind.LogicalAnd or
            BinaryOperatorKind.LogicalOr => left.Type, // Should be Boolean — use left for now
            _ => left.Type, // Arithmetic: result is same type as operands
        };
    }

    private static BinaryOperatorKind MapOperator(string? lexeme)
    {
        return lexeme switch
        {
            "+" => BinaryOperatorKind.Add,
            "-" => BinaryOperatorKind.Subtract,
            "*" => BinaryOperatorKind.Multiply,
            "/" => BinaryOperatorKind.Divide,
            "%" => BinaryOperatorKind.Modulo,
            "==" => BinaryOperatorKind.Equal,
            "!=" => BinaryOperatorKind.NotEqual,
            "<" => BinaryOperatorKind.LessThan,
            ">" => BinaryOperatorKind.GreaterThan,
            "<=" => BinaryOperatorKind.LessThanOrEqual,
            ">=" => BinaryOperatorKind.GreaterThanOrEqual,
            "&&" => BinaryOperatorKind.LogicalAnd,
            "||" => BinaryOperatorKind.LogicalOr,
            _ => BinaryOperatorKind.Add, // fallback
        };
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