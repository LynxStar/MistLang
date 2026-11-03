using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

// Facts describe the selected fixed-width result, including its wrapping, rather than a wider intermediate.
internal static class IntegerOperandFold
{
    public static long? Fold(IMistType type, BinaryOperatorKind op, long left, long right)
    {
        Int128? result = op switch
        {
            BinaryOperatorKind.Add => (Int128)left + right,
            BinaryOperatorKind.Subtract => (Int128)left - right,
            BinaryOperatorKind.Multiply => (Int128)left * right,
            BinaryOperatorKind.Divide when right != 0 => (Int128)left / right,
            BinaryOperatorKind.Modulo when right != 0 => (Int128)left % right,
            _ => null,
        };

        if(result is not { } value)
        {
            return null;
        }

        return type.FullyQualifiedName switch
        {
            "Standard.SByte" => unchecked((sbyte)value),
            "Standard.Byte" => unchecked((byte)value),
            "Standard.Int16" => unchecked((short)value),
            "Standard.UInt16" => unchecked((ushort)value),
            "Standard.Int32" => unchecked((int)value),
            "Standard.UInt32" => unchecked((uint)value),
            "Standard.Int64" => unchecked((long)value),
            "Standard.UInt64" when unchecked((ulong)value) <= long.MaxValue => (long)unchecked((ulong)value),
            _ => null,
        };
    }
}