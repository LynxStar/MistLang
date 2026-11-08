using System.Numerics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramAnalysis.Facts;

internal static class Terms
{
    public static Term? Read(Expression expression, Bindings bindings)
    {
        return Read(expression, read => Holder(read, bindings) is { } holder ? bindings.Get(holder) : null);
    }

    public static Term? Read(Expression expression, Func<Expression, Value?> readHolder)
    {
        if(Requirement.Constant(expression) is { } constant)
        {
            return new Term(constant);
        }

        if(expression is LiteralExpression literal && IntegralConstant.Of(literal.Value) is { } large)
        {
            return new Term((BigInteger)large);
        }

        switch(expression)
        {
            case LocalReferenceExpression or FieldReferenceExpression or MemberAccessExpression { IsConditional: false, IsFailed: false }:
                return readHolder(expression) is { } value ? new Term(value) : null;

            case UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operator: null or { Modifiers.IsIntrinsic: true } } unary:
                return Read(unary.Operand, readHolder)?.Scale(-1);

            case BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } binary:
                var left = Read(binary.Left, readHolder);
                var right = Read(binary.Right, readHolder);
                return left is null || right is null ? null : binary.OperatorKind switch
                {
                    BinaryOperatorKind.Add => left.Add(right),
                    BinaryOperatorKind.Subtract => left.Subtract(right),
                    BinaryOperatorKind.Multiply when left.Coefficients.Count == 0 => right.Scale(left.Constant),
                    BinaryOperatorKind.Multiply when right.Coefficients.Count == 0 => left.Scale(right.Constant),
                    _ => null,
                };

            default:
                return null;
        }
    }

    public static object? Holder(Expression expression, Bindings bindings)
    {
        return expression switch
        {
            LocalReferenceExpression local => local.Local,
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { IsConditional: false, IsFailed: false, Receiver: ThisExpression, Member: FieldResult field } => field.Field,
            MemberAccessExpression { IsConditional: false, IsFailed: false, Member: { } member } access
                when Holder(access.Receiver, bindings) is { } receiver && bindings.Get(receiver) is { } value
                => new MemberHolder(value, member switch
                {
                    FieldResult field => field.Field,
                    PropertyResult property => property.Property,
                    TypeResult type => type.Type,
                    _ => member,
                }),
            _ => null,
        };
    }
}