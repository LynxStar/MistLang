using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Expressions;

/// <summary>
/// Entry point for expression binding. Unwraps the assignment layer and dispatches
/// to the precedence chain, ultimately reaching a primary expression.
/// </summary>
internal static class ExpressionBinder
{
    public static Expression BindExpression(ExpressionNode expression, BindingContext ctx, BuilderContext context)
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

        return OperatorBinder.BindVoidCoalescing(assignment.Target, ctx, context);
    }

    internal static Expression BindPrimary(PrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return node switch
        {
            IntegerLiteralExpressionPrimaryNode integer => LiteralBinder.BindIntegerLiteral(integer, context),
            StringLiteralExpressionPrimaryNode stringLit => LiteralBinder.BindStringLiteral(stringLit, context),
            IdentifierExpressionPrimaryNode identifier => IdentifierBinder.BindIdentifier(identifier, ctx, context),
            _ => new UnresolvedExpression(node),
        };
    }
}