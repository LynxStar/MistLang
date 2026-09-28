using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// An integer constant reaches any integral type it fits, which no conversion between TYPES can say: an
/// int does not reach a byte, and <c>2</c> does. So every place that asks whether a value reaches a slot --
/// an assignment, an argument among several overloads, a cast operator's parameter -- asks this beside the
/// type relation rather than through it.
/// </summary>
internal static class ConstantConversion
{
    private static readonly Dictionary<string, (Int128 Min, Int128 Max)> IntegralRanges = new(StringComparer.Ordinal)
    {
        [WellKnownTypes.SByte.FullyQualifiedName] = (sbyte.MinValue, sbyte.MaxValue),
        [WellKnownTypes.Byte.FullyQualifiedName] = (byte.MinValue, byte.MaxValue),
        [WellKnownTypes.Int16.FullyQualifiedName] = (short.MinValue, short.MaxValue),
        [WellKnownTypes.UInt16.FullyQualifiedName] = (ushort.MinValue, ushort.MaxValue),
        [WellKnownTypes.UInt32.FullyQualifiedName] = (uint.MinValue, uint.MaxValue),
        [WellKnownTypes.UInt64.FullyQualifiedName] = (ulong.MinValue, ulong.MaxValue),
    };

    public static bool Fits(Expression value, IMistType target)
    {
        return IntegerConstant(value) is { } constant && Fits(constant, value.Type, target);
    }

    public static bool Fits(Int128 constant, IMistType from, IMistType to)
    {
        to = Unrefined(to);

        if(to is UnionType union)
        {
            return union.Constituents.Any(constituent => Fits(constant, from, constituent));
        }

        if(from.FullyQualifiedName == WellKnownTypes.Int64.FullyQualifiedName)
        {
            return to.FullyQualifiedName == WellKnownTypes.UInt64.FullyQualifiedName && constant >= 0;
        }

        return from.FullyQualifiedName == WellKnownTypes.Int32.FullyQualifiedName
            && IntegralRanges.TryGetValue(to.FullyQualifiedName, out var range)
            && constant >= range.Min
            && constant <= range.Max;
    }

    public static Int128? IntegerConstant(Expression expression)
    {
        return expression switch
        {
            LiteralExpression literal => IntegralConstant.Of(literal.Value),
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate } negated => -IntegerConstant(negated.Operand),
            CloneExpression clone => IntegerConstant(clone.Source),
            _ => null,
        };
    }

    private static IMistType Unrefined(IMistType type)
    {
        return type is RefinedType refined ? Unrefined(refined.BaseType) : type;
    }
}