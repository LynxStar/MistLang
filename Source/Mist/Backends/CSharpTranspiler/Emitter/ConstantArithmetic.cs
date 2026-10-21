using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Integral arithmetic over constants. Mist's arithmetic wraps on overflow whether its operands are constant or
/// not (standard-library Requirements 3.4 and 12.1), and dividing by zero fails when it runs, as any pure
/// expression may (purity spec, Deferred). Roslyn folds the same arithmetic at compile time in a checked context
/// and refuses it instead: an overflowing fold unless it is unchecked, a constant division by zero always. Only
/// the expressions Roslyn would refuse are marked, so every constant it already folds keeps its spelling.
/// </summary>
internal static class ConstantArithmetic
{
    public static bool Overflows(Expression expression)
    {
        return Fold(expression) is { Overflowed: true };
    }

    public static bool DividesByZero(BinaryExpression division)
    {
        return division.OperatorKind is BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo
            && IsBuiltIn(division.Operator)
            && Fold(division.Left) is not null
            && Fold(division.Right) is { Value: var divisor } && divisor == 0;
    }

    private static (Int128 Value, bool Overflowed)? Fold(Expression expression)
    {
        return expression switch
        {
            LiteralExpression literal => IntegralConstant.Of(literal.Value) is { } value ? (value, false) : null,
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate } negation when IsBuiltIn(negation.Operator)
                => Fold(negation.Operand) is { } operand ? Checked(-operand.Value, operand.Overflowed, negation) : null,
            BinaryExpression binary when IsBuiltIn(binary.Operator) => Arithmetic(binary),
            CastExpression cast when cast.Operator is null or { Modifiers.IsIntrinsic: true } && IntegralConstant.IsIntegral(cast.Type)
                => Fold(cast.Operand) is { } operand ? Checked(operand.Value, operand.Overflowed, cast) : null,
            ImplicitConversionExpression conversion when IntegralConstant.IsIntegral(conversion.Type) => Fold(conversion.Operand),
            _ => ClrConstant.Of(expression) is { } stored && IntegralConstant.Of(stored.Value) is { } value ? (value, false) : null,
        };
    }

    private static (Int128 Value, bool Overflowed)? Arithmetic(BinaryExpression binary)
    {
        if(Fold(binary.Left) is not { } left || Fold(binary.Right) is not { } right)
        {
            return null;
        }

        Int128? value = binary.OperatorKind switch
        {
            BinaryOperatorKind.Add => left.Value + right.Value,
            BinaryOperatorKind.Subtract => left.Value - right.Value,
            BinaryOperatorKind.Multiply when right.Value == 0 || Int128.Abs(left.Value) <= Int128.MaxValue / Int128.Abs(right.Value) => left.Value * right.Value,
            BinaryOperatorKind.Multiply => Int128.MaxValue,
            BinaryOperatorKind.Divide when right.Value != 0 => left.Value / right.Value,
            BinaryOperatorKind.Modulo when right.Value != 0 => left.Value % right.Value,
            _ => null,
        };

        return value is { } folded ? Checked(folded, left.Overflowed || right.Overflowed, binary) : null;
    }

    private static (Int128 Value, bool Overflowed) Checked(Int128 value, bool overflowed, Expression result)
    {
        return (value, overflowed || !IntegralConstant.Fits(value, result.Type));
    }

    private static bool IsBuiltIn(Operator? declared)
    {
        return declared is null or { Modifiers.IsIntrinsic: true };
    }
}