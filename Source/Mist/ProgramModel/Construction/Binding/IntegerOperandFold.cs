using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

// Facts describe the selected fixed-width result, including its wrapping, rather than a wider intermediate.
internal static class IntegerOperandFold
{
    public static Int128? Fold(IMistType type, BinaryOperatorKind op, Int128 left, Int128 right)
    {
        Int128? result = op switch
        {
            BinaryOperatorKind.Add => left + right,
            BinaryOperatorKind.Subtract => left - right,
            BinaryOperatorKind.Multiply => unchecked(left * right),
            BinaryOperatorKind.Divide when right != 0 => left / right,
            BinaryOperatorKind.Modulo when right != 0 => left % right,
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
            "Standard.UInt64" => unchecked((ulong)value),
            _ => null,
        };
    }
}