using System.Numerics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Facts;

internal static class IntegralTypes
{
    private static Dictionary<string, IntegralInterval> Ranges { get; } = new(StringComparer.Ordinal)
    {
        [WellKnownTypes.SByte.FullyQualifiedName] = new(sbyte.MinValue, sbyte.MaxValue),
        [WellKnownTypes.Byte.FullyQualifiedName] = new(byte.MinValue, byte.MaxValue),
        [WellKnownTypes.Int16.FullyQualifiedName] = new(short.MinValue, short.MaxValue),
        [WellKnownTypes.UInt16.FullyQualifiedName] = new(ushort.MinValue, ushort.MaxValue),
        [WellKnownTypes.Int32.FullyQualifiedName] = new(int.MinValue, int.MaxValue),
        [WellKnownTypes.UInt32.FullyQualifiedName] = new(uint.MinValue, uint.MaxValue),
        [WellKnownTypes.Int64.FullyQualifiedName] = new(long.MinValue, long.MaxValue),
        [WellKnownTypes.UInt64.FullyQualifiedName] = new(BigInteger.Zero, new BigInteger(ulong.MaxValue)),
        [WellKnownTypes.Char.FullyQualifiedName] = new(char.MinValue, char.MaxValue),
    };

    public static IntegralInterval? Range(IMistType type)
    {
        type = Requirement.Unrefined(type);
        if(type is LiftedType lifted)
        {
            return Range(lifted.Type);
        }

        if(type is UnknownValueType unknown)
        {
            return Range(unknown.Domain);
        }

        if(type is CapturedValue captured)
        {
            return Range(captured.Domain);
        }

        if(type is TypeParameter { IsDependent: true, Domain: { } domain })
        {
            return Range(domain);
        }

        if(type is EnumType enumeration)
        {
            return enumeration.UnderlyingType is { } underlying ? Range(underlying) : new IntegralInterval(int.MinValue, int.MaxValue);
        }

        return Ranges.TryGetValue(type.FullyQualifiedName, out var range) ? range : null;
    }
}