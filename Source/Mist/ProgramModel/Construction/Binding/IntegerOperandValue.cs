using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Binding;

// Exact values retain all UInt64 bits; interval facts remain in the shared signed refinement domain.
internal static class IntegerOperandValue
{
    public static Int128? Exact(BindingContext ctx, Scope scope, Expression expression)
    {
        switch(expression)
        {
            case UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operator: null or { Modifiers.IsIntrinsic: true } } unary
                when Exact(ctx, scope, unary.Operand) is { } original:
                return IntegerOperandFold.Fold(unary.Type, BinaryOperatorKind.Subtract, 0, original);

            case BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } binary
                when Exact(ctx, scope, binary.Left) is { } left && Exact(ctx, scope, binary.Right) is { } right:
                return IntegerOperandFold.Fold(binary.Type, binary.OperatorKind, left, right);

            case LocalReferenceExpression local when !OperandFacts.IsMutable(ctx, local.Local):
                if(ctx.IntegerConstants.TryGetValue(local.Local, out var known))
                {
                    return known;
                }

                var values = OperandFacts.FactFor(ctx, scope, local.Local);
                return values.Low is { } low && values.High == low ? low : null;

            case ImplicitConversionExpression { Operator.Modifiers.IsIntrinsic: true } conversion
                when Requirement.IsIntegral(conversion.Type) && Exact(ctx, scope, conversion.Operand) is { } converted:
                return IntegerOperandFold.Fold(conversion.Type, BinaryOperatorKind.Add, converted, 0);

            case CloneExpression clone:
                return Exact(ctx, scope, clone.Source);

            case LiteralExpression literal when IntegralConstant.Of(literal.Value) is { } value:
                return value;

            case UnaryExpression or BinaryExpression:
                return null;

            default:
                return ClrConstant.Of(expression) is { } stored && IntegralConstant.Of(stored.Value) is { } number
                    ? number : Requirement.Constant(expression);
        }
    }
}