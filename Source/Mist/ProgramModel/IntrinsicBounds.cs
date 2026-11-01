using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel;

internal static class IntrinsicBounds
{
    private static Dictionary<string, (object Min, object Max)> Values { get; } = new(StringComparer.Ordinal)
    {
        ["Standard.SByte"] = (sbyte.MinValue, sbyte.MaxValue),
        ["Standard.Byte"] = (byte.MinValue, byte.MaxValue),
        ["Standard.Int16"] = (short.MinValue, short.MaxValue),
        ["Standard.UInt16"] = (ushort.MinValue, ushort.MaxValue),
        ["Standard.Int32"] = (int.MinValue, int.MaxValue),
        ["Standard.UInt32"] = (uint.MinValue, uint.MaxValue),
        ["Standard.Int64"] = (long.MinValue, long.MaxValue),
        ["Standard.UInt64"] = (ulong.MinValue, ulong.MaxValue),
        ["Standard.Char"] = (char.MinValue, char.MaxValue),
        ["Standard.Single"] = (float.MinValue, float.MaxValue),
        ["Standard.Double"] = (double.MinValue, double.MaxValue),
        ["Standard.Decimal"] = (decimal.MinValue, decimal.MaxValue),
    };

    public static LiteralExpression? Of(Field field, SyntaxNode node)
    {
        if(field is not { Modifiers: { IsIntrinsic: true, IsStatic: true, IsReadonly: true }, DeclaringType: Model.Types.ConcreteType { IsIntrinsic: true } }
            || field.DeclaringType.FullyQualifiedName != field.Type.FullyQualifiedName
            || !Values.TryGetValue(field.Type.FullyQualifiedName, out var bounds))
        {
            return null;
        }

        return field.Name switch
        {
            "MinValue" => new LiteralExpression(node, bounds.Min, field.Type),
            "MaxValue" => new LiteralExpression(node, bounds.Max, field.Type),
            _ => null,
        };
    }
}