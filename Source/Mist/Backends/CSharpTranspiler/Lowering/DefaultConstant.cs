using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The value a compile-time constant default computes, as the emitted C# computes it, or null where that
/// is not known here. Integral values are carried as Int128 and wrap to their type, as Mist's arithmetic
/// does; floating-point and decimal values compute in their own CLR type.
/// </summary>
internal static class DefaultConstant
{
    public static bool IsZero(object constant)
    {
        return constant switch
        {
            Int128 number => number == 0,
            bool flag => !flag,
            char character => character == '\0',
            double real => BitConverter.DoubleToInt64Bits(real) == 0,
            float real => BitConverter.SingleToInt32Bits(real) == 0,
            decimal number => decimal.GetBits(number).All(part => part == 0),
            _ => false,
        };
    }

    public static object? Evaluate(Expression expression)
    {
        return expression switch
        {
            LiteralExpression literal => Normalize(literal.Value),
            DefaultExpression { Type: var type } => Zero(type),
            UnaryExpression unary when IsBuiltIn(unary.Operator) => Unary(unary),
            BinaryExpression binary when IsBuiltIn(binary.Operator) => Binary(binary),
            CastExpression { IsConditional: false, Type: StructType { IsIntrinsic: true } } cast when IsBuiltIn(cast.Operator)
                => Evaluate(cast.Operand) is { } operand ? Convert(operand, cast.Type) : null,
            ImplicitConversionExpression conversion when IsBuiltIn(conversion.Operator)
                => Evaluate(conversion.Operand) is { } operand ? Convert(operand, conversion.Type) : null,
            CloneExpression clone => Evaluate(clone.Source),
            ResultIdentityExpression identity => Evaluate(identity.Operand),
            _ => ClrConstant.Of(expression) is { } stored ? Normalize(stored.Value) : null,
        };
    }

    private static object? Normalize(object? value)
    {
        return IntegralConstant.Of(value) is { } integral ? integral : value is bool or char or double or float or decimal ? value : null;
    }

    private static object? Zero(IMistType type)
    {
        return type is StructType { IsIntrinsic: true } ? Convert(Int128.Zero, type) ?? Convert(false, type) ?? Convert('\0', type) : null;
    }

    private static object? Unary(UnaryExpression unary)
    {
        return (unary.OperatorKind, Evaluate(unary.Operand)) switch
        {
            (UnaryOperatorKind.Negate, Int128 number) => Wrap(-number, unary.Type),
            (UnaryOperatorKind.Negate, double real) => -real,
            (UnaryOperatorKind.Negate, float real) => -real,
            (UnaryOperatorKind.Negate, decimal number) => -number,
            (UnaryOperatorKind.LogicalNot, bool flag) => !flag,
            _ => null,
        };
    }

    private static object? Binary(BinaryExpression binary)
    {
        if(binary.OperatorKind is BinaryOperatorKind.LogicalAnd or BinaryOperatorKind.LogicalOr)
        {
            var decides = binary.OperatorKind == BinaryOperatorKind.LogicalOr;
            return Evaluate(binary.Left) is bool left
                ? left == decides ? decides : Evaluate(binary.Right) as bool?
                : null;
        }

        return (Evaluate(binary.Left), Evaluate(binary.Right)) switch
        {
            (Int128 left, Int128 right) => Compare(binary.OperatorKind, left.CompareTo(right)) ?? Integral(binary, left, right),
            (double left, double right) => Ordered(binary.OperatorKind, left, right) ?? Real(binary.OperatorKind, left, right),
            (float left, float right) => Ordered(binary.OperatorKind, left, right) ?? Real(binary.OperatorKind, left, right),
            (decimal left, decimal right) => Compare(binary.OperatorKind, left.CompareTo(right)) ?? Decimal(binary.OperatorKind, left, right),
            (char left, char right) => Compare(binary.OperatorKind, left.CompareTo(right)),
            (bool left, bool right) => binary.OperatorKind switch
            {
                BinaryOperatorKind.Equal => left == right,
                BinaryOperatorKind.NotEqual => left != right,
                _ => null,
            },
            _ => null,
        };
    }

    private static object? Integral(BinaryExpression binary, Int128 left, Int128 right)
    {
        return binary.OperatorKind switch
        {
            BinaryOperatorKind.Add => Wrap(left + right, binary.Type),
            BinaryOperatorKind.Subtract => Wrap(left - right, binary.Type),
            BinaryOperatorKind.Multiply => Wrap(unchecked(left * right), binary.Type),
            BinaryOperatorKind.Divide when right != 0 && IntegralConstant.Fits(left / right, binary.Type) => left / right,
            BinaryOperatorKind.Modulo when right != 0 && IntegralConstant.Fits(left / right, binary.Type) => left % right,
            _ => null,
        };
    }

    private static object? Real<T>(BinaryOperatorKind kind, T left, T right)
        where T : System.Numerics.IFloatingPointIeee754<T>
    {
        return kind switch
        {
            BinaryOperatorKind.Add => left + right,
            BinaryOperatorKind.Subtract => left - right,
            BinaryOperatorKind.Multiply => left * right,
            BinaryOperatorKind.Divide => left / right,
            BinaryOperatorKind.Modulo => left % right,
            _ => null,
        };
    }

    private static object? Decimal(BinaryOperatorKind kind, decimal left, decimal right)
    {
        try
        {
            return kind switch
            {
                BinaryOperatorKind.Add => left + right,
                BinaryOperatorKind.Subtract => left - right,
                BinaryOperatorKind.Multiply => left * right,
                BinaryOperatorKind.Divide when right != 0 => left / right,
                BinaryOperatorKind.Modulo when right != 0 => left % right,
                _ => null,
            };
        }
        catch(OverflowException)
        {
            return null;
        }
    }

    private static object? Ordered<T>(BinaryOperatorKind kind, T left, T right)
        where T : System.Numerics.IFloatingPointIeee754<T>
    {
        return kind switch
        {
            BinaryOperatorKind.Equal => left == right,
            BinaryOperatorKind.NotEqual => left != right,
            BinaryOperatorKind.LessThan => left < right,
            BinaryOperatorKind.GreaterThan => left > right,
            BinaryOperatorKind.LessThanOrEqual => left <= right,
            BinaryOperatorKind.GreaterThanOrEqual => left >= right,
            _ => null,
        };
    }

    private static object? Compare(BinaryOperatorKind kind, int order)
    {
        return kind switch
        {
            BinaryOperatorKind.Equal => order == 0,
            BinaryOperatorKind.NotEqual => order != 0,
            BinaryOperatorKind.LessThan => order < 0,
            BinaryOperatorKind.GreaterThan => order > 0,
            BinaryOperatorKind.LessThanOrEqual => order <= 0,
            BinaryOperatorKind.GreaterThanOrEqual => order >= 0,
            _ => null,
        };
    }

    private static object? Convert(object value, IMistType target)
    {
        var name = target.FullyQualifiedName;
        var integral = value switch
        {
            Int128 number => number,
            char character => character,
            _ => (Int128?)null,
        };

        if(IntegralConstant.IsIntegral(target))
        {
            return integral is { } fitted && IntegralConstant.Fits(fitted, target) ? fitted : null;
        }

        var signed = integral is { } whole && whole >= long.MinValue && whole <= long.MaxValue;

        return value switch
        {
            Int128 number when name == WellKnownTypes.Double.FullyQualifiedName => signed ? (long)number : (double)(ulong)number,
            Int128 number when name == WellKnownTypes.Single.FullyQualifiedName => signed ? (long)number : (float)(ulong)number,
            Int128 number when name == WellKnownTypes.Decimal.FullyQualifiedName => signed ? (long)number : (decimal)(ulong)number,
            float real when name == WellKnownTypes.Double.FullyQualifiedName => (double)real,
            _ when name == KindOf(value) => value,
            _ => null,
        };
    }

    private static string? KindOf(object value)
    {
        return value switch
        {
            double => WellKnownTypes.Double.FullyQualifiedName,
            float => WellKnownTypes.Single.FullyQualifiedName,
            decimal => WellKnownTypes.Decimal.FullyQualifiedName,
            bool => WellKnownTypes.Boolean.FullyQualifiedName,
            char => WellKnownTypes.Char.FullyQualifiedName,
            _ => null,
        };
    }

    private static object? Wrap(Int128 value, IMistType type)
    {
        return type.FullyQualifiedName switch
        {
            var name when name == WellKnownTypes.SByte.FullyQualifiedName => (Int128)unchecked((sbyte)value),
            var name when name == WellKnownTypes.Byte.FullyQualifiedName => (Int128)unchecked((byte)value),
            var name when name == WellKnownTypes.Int16.FullyQualifiedName => (Int128)unchecked((short)value),
            var name when name == WellKnownTypes.UInt16.FullyQualifiedName => (Int128)unchecked((ushort)value),
            var name when name == WellKnownTypes.Int32.FullyQualifiedName => (Int128)unchecked((int)value),
            var name when name == WellKnownTypes.UInt32.FullyQualifiedName => (Int128)unchecked((uint)value),
            var name when name == WellKnownTypes.Int64.FullyQualifiedName => (Int128)unchecked((long)value),
            var name when name == WellKnownTypes.UInt64.FullyQualifiedName => (Int128)unchecked((ulong)value),
            _ => null,
        };
    }

    private static bool IsBuiltIn(Method? declared)
    {
        return declared is null or { Modifiers.IsIntrinsic: true };
    }
}