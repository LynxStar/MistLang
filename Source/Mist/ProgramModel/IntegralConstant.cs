using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel;

/// <summary>
/// An integral constant is held boxed as the CLR primitive of its own type, never in one carrier shared by
/// all eight: no primitive holds both long.MinValue and ulong.MaxValue. Reading one widens it to
/// Int128, which holds every value of every integral type and every sum or difference of two of them.
/// </summary>
internal static class IntegralConstant
{
    private static readonly Dictionary<string, (Int128 Min, Int128 Max, Func<Int128, object> Box)> Integral = new(StringComparer.Ordinal)
    {
        [WellKnownTypes.SByte.FullyQualifiedName] = (sbyte.MinValue, sbyte.MaxValue, value => (sbyte)value),
        [WellKnownTypes.Byte.FullyQualifiedName] = (byte.MinValue, byte.MaxValue, value => (byte)value),
        [WellKnownTypes.Int16.FullyQualifiedName] = (short.MinValue, short.MaxValue, value => (short)value),
        [WellKnownTypes.UInt16.FullyQualifiedName] = (ushort.MinValue, ushort.MaxValue, value => (ushort)value),
        [WellKnownTypes.Int32.FullyQualifiedName] = (int.MinValue, int.MaxValue, value => (int)value),
        [WellKnownTypes.UInt32.FullyQualifiedName] = (uint.MinValue, uint.MaxValue, value => (uint)value),
        [WellKnownTypes.Int64.FullyQualifiedName] = (long.MinValue, long.MaxValue, value => (long)value),
        [WellKnownTypes.UInt64.FullyQualifiedName] = (ulong.MinValue, ulong.MaxValue, value => (ulong)value),
    };

    public static Int128? Of(object? value)
    {
        return value switch
        {
            sbyte number => number,
            byte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number => number,
            _ => null,
        };
    }

    public static bool IsIntegral(IMistType type)
    {
        return Integral.ContainsKey(type.FullyQualifiedName);
    }

    public static bool Fits(Int128 value, IMistType type)
    {
        return Integral.TryGetValue(type.FullyQualifiedName, out var integral)
            && value >= integral.Min
            && value <= integral.Max;
    }

    public static object? Box(Int128 value, IMistType type)
    {
        return Fits(value, type) ? Integral[type.FullyQualifiedName].Box(value) : null;
    }

    public static Int128? Fold(Expression? expression)
    {
        var value = expression switch
        {
            LiteralExpression literal => Of(literal.Value),
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate } negated => -Fold(negated.Operand),
            BinaryExpression binary => Arithmetic(binary),
            ImplicitConversionExpression conversion => Fold(conversion.Operand),
            CloneExpression clone => Fold(clone.Source),
            _ => null,
        };

        return value is { } folded && Fits(folded, expression!.Type) ? folded : null;
    }

    private static Int128? Arithmetic(BinaryExpression binary)
    {
        if(Fold(binary.Left) is not { } left || Fold(binary.Right) is not { } right)
        {
            return null;
        }

        return binary.OperatorKind switch
        {
            BinaryOperatorKind.Add => left + right,
            BinaryOperatorKind.Subtract => left - right,
            BinaryOperatorKind.Multiply when right == 0 || Int128.Abs(left) <= Int128.MaxValue / Int128.Abs(right) => left * right,
            BinaryOperatorKind.Divide when right != 0 => left / right,
            BinaryOperatorKind.Modulo when right != 0 => left % right,
            _ => null,
        };
    }
}