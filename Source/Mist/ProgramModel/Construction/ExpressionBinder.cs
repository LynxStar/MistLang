using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

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
        var result = BindPrimary(node.Target, ctx, context);

        foreach(var suffix in node.Suffix)
        {
            result = BindPostfixSuffix(suffix, result, ctx, context);
        }

        return result;
    }

    private static Expression BindPostfixSuffix(
        PostfixSuffixNode suffix, Expression receiver, BindingContext ctx, BuilderContext context)
    {
        switch(suffix)
        {
            case InvocationSuffixPostfixSuffixNode invocation:
                {
                    var arguments = BindArguments(invocation.Arguments, ctx, context);
                    var returnType = receiver.Type;
                    return new InvocationExpression(suffix, receiver, arguments, returnType);
                }

            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                {
                    var memberName = memberAccess.Name.Lexeme;
                    var memberType = ResolveMemberType(receiver, memberName);
                    return new MemberAccessExpression(suffix, receiver, memberName, memberType);
                }

            case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    // .name(args) is syntactically combined member access + invocation.
                    var memberName = methodCall.Name.Lexeme;
                    var memberType = ResolveMemberType(receiver, memberName);
                    var memberExpr = new MemberAccessExpression(suffix, receiver, memberName, memberType);
                    var arguments = BindArguments(methodCall.Arguments, ctx, context);
                    return new InvocationExpression(suffix, memberExpr, arguments, memberType);
                }

            default:
                return new UnresolvedExpression(suffix);
        }
    }

    private static IReadOnlyList<Expression> BindArguments(
        ArgumentListNode? argumentList, BindingContext ctx, BuilderContext context)
    {
        if(argumentList is null)
        {
            return [];
        }

        var expressions = argumentList.GetAllExpressions();
        var bound = new List<Expression>(expressions.Length);
        foreach(var expr in expressions)
        {
            bound.Add(BindExpression(expr, ctx, context));
        }

        return bound;
    }

    /// <summary>
    /// Resolves the type of a member accessed on a receiver. For now, looks up methods
    /// on the receiver's type and returns the method's return type. Falls back to the
    /// error type for members that cannot be resolved yet.
    /// </summary>
    private static IMistType ResolveMemberType(Expression receiver, string memberName)
    {
        // If the receiver is a type reference (static access like Console.WriteLine),
        // look up the member on that type.
        var lookupType = receiver is TypeReferenceExpression typeRef
            ? typeRef.ReferencedType
            : receiver.Type;

        if(lookupType is MemberType memberType)
        {
            foreach(var method in memberType.MethodsList)
            {
                if(method.Name == memberName)
                {
                    return method.ReturnType;
                }
            }
        }

        return ErrorType.Instance;
    }

    private static Expression BindPrimary(PrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return node switch
        {
            IntegerLiteralExpressionPrimaryNode integer => BindIntegerLiteral(integer, context),
            StringLiteralExpressionPrimaryNode stringLit => BindStringLiteral(stringLit, context),
            IdentifierExpressionPrimaryNode identifier => BindIdentifier(identifier, ctx, context),
            _ => new UnresolvedExpression(node),
        };
    }

    private static Expression BindIntegerLiteral(IntegerLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        var (value, wellKnown) = ParseIntegerLiteral(node.IntegerLiteral.Lexeme);
        var type = context.TypeResolver.LookupWellKnown(wellKnown);
        return new LiteralExpression(node, value, type);
    }

    private static Expression BindStringLiteral(StringLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        // Strip surrounding quotes to get the string value.
        var lexeme = node.StringLiteral.Lexeme;
        var value = lexeme.Length >= 2 ? lexeme[1..^1] : lexeme;
        var type = context.TypeResolver.LookupWellKnown(WellKnownTypes.String);
        return new LiteralExpression(node, value, type);
    }

    private static Expression BindIdentifier(IdentifierExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var name = node.Name.Lexeme;

        // 1. Local variable or parameter.
        var local = ctx.Scope.LookupLocal(name);
        if(local is not null)
        {
            return new LocalReferenceExpression(node, local);
        }

        // 2. Method on the enclosing type (sibling method call like RunDemo).
        if(ctx.EnclosingType is not null)
        {
            foreach(var method in ctx.EnclosingType.MethodsList)
            {
                if(method.Name == name)
                {
                    return new MethodReferenceExpression(node, method);
                }
            }
        }

        // 3. Type name (for static member access like Console).
        var resolvedType = context.TypeResolver.ResolveNamedType(name, ctx.Scope, node);
        if(resolvedType is not ErrorType)
        {
            return new TypeReferenceExpression(node, resolvedType);
        }

        // Cannot resolve — deferred.
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