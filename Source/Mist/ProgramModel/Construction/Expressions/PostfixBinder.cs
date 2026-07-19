using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Expressions;

/// <summary>
/// Handles postfix expressions: binds the primary target, then folds each
/// suffix (member access, invocation, method call) onto the result.
/// </summary>
internal static class PostfixBinder
{
    internal static Expression BindPostfix(PostfixNode node, BindingContext ctx, BuilderContext context)
    {
        var result = ExpressionBinder.BindPrimary(node.Target, ctx, context);

        foreach(var suffix in node.Suffix)
        {
            result = BindPostfixSuffix(suffix, result, ctx, context);
        }

        return result;
    }

    private static Expression BindPostfixSuffix(PostfixSuffixNode suffix, Expression receiver, BindingContext ctx, BuilderContext context)
    {
        switch(suffix)
        {
            case InvocationSuffixPostfixSuffixNode invocation:
                return BindInvocation(suffix, receiver, invocation.Arguments, ctx, context);

            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                return BindMemberAccess(suffix, receiver, memberAccess.Name.Lexeme);

            case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    var memberExpr = BindMemberAccess(suffix, receiver, methodCall.Name.Lexeme);
                    return BindInvocation(suffix, memberExpr, methodCall.Arguments, ctx, context);
                }

            case IndexSuffixPostfixSuffixNode indexSuffix:
                {
                    var index = ExpressionBinder.BindExpression(indexSuffix.Index, ctx, context);
                    return new ElementAccessExpression(suffix, receiver, index, receiver.Type);
                }

            default:
                return new UnresolvedExpression(suffix);
        }
    }

    private static InvocationExpression BindInvocation(Storm.Runtime.SyntaxNode node, Expression receiver, ArgumentListNode? arguments, BindingContext ctx, BuilderContext context)
    {
        var bound = BindArguments(arguments, ctx, context);
        return new InvocationExpression(node, receiver, bound, receiver.Type);
    }

    private static MemberAccessExpression BindMemberAccess(Storm.Runtime.SyntaxNode node, Expression receiver, string memberName)
    {
        var memberType = ResolveMemberType(receiver, memberName);
        return new MemberAccessExpression(node, receiver, memberName, memberType);
    }

    private static IReadOnlyList<Expression> BindArguments(ArgumentListNode? argumentList, BindingContext ctx, BuilderContext context)
    {
        if(argumentList is null)
        {
            return [];
        }

        var expressions = argumentList.GetAllExpressions();
        var bound = new List<Expression>(expressions.Length);
        foreach(var expr in expressions)
        {
            bound.Add(ExpressionBinder.BindExpression(expr, ctx, context));
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
}