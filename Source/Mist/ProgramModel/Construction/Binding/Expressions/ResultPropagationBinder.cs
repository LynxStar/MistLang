using Mist.Generated;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static class ResultPropagationBinder
{
    public static Expression Bind(SyntaxNode node, Expression operand, BindingContext ctx, BuilderContext context)
    {
        if(ResultTypes.Of(operand.Type) is not { } result)
        {
            return new ResultIdentityExpression(node, operand);
        }

        var interner = context.ProgramModel.TypeInterner;
        var error = new ElementReadExpression(node, result.Error, operand);
        var target = ResultTypes.Of(ctx.ReturnType);
        var returned = target is null ? ImplicitConversions.At(error, ctx.ReturnType, interner) : new ResultConstructionExpression(node,
            ImplicitConversions.At(error, target.Error, interner), target.Type, ResultCase.Failure);

        var inspection = result.Inspection(interner);
        var success = inspection.Constituents.Single(part => part is ITupleType tuple && Requirement.ValuesOf(tuple.ElementTypes[0])?.Contains(1) is true);
        var failure = inspection.Constituents.Single(part => !ReferenceEquals(part, success));
        return new ResultPropagationExpression(node, operand, result, inspection, success, failure, error, new ReturnStatement(returned, ctx.ReturnType));
    }

    public static IMistType? ProjectionTarget(PostfixNode node, BindingContext ctx)
    {
        if(node.Target is TypeLiteralExpressionPrimaryNode or ParenthesizedExpressionPrimaryNode
            && node.Suffix is [PropagationSuffixPostfixSuffixNode])
        {
            return ctx.TargetType;
        }

        if(node.Target is not (TypeLiteralExpressionPrimaryNode or ParenthesizedExpressionPrimaryNode)
            || node.Suffix is not [MethodCallSuffixPostfixSuffixNode { Arguments: null, TypeArgs: null } projection]
            || ResultTypes.Of(ctx.TargetType) is not { } target)
        {
            return null;
        }

        return projection.Name.Lexeme switch
        {
            "AsSuccess" => target.Success,
            "AsFailure" => target.Error,
            _ => null,
        };
    }
}