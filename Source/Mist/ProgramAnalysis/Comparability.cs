using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a relational pattern can order a value of a type: a primitive numeric, a char, or a
/// refinement or enum over one. A user type's declared comparison operators do not count, since a
/// relational pattern is not a call to one, and neither does a type parameter under any constraint.
/// A pattern in a body and a refinement predicate on a declaration ask this one question, so the two
/// cannot disagree about a type.
/// </summary>
internal static class Comparability
{
    private static readonly string[] ComparableNames =
    [
        WellKnownTypes.Byte.FullyQualifiedName, WellKnownTypes.SByte.FullyQualifiedName,
        WellKnownTypes.Int16.FullyQualifiedName, WellKnownTypes.UInt16.FullyQualifiedName,
        WellKnownTypes.Int32.FullyQualifiedName, WellKnownTypes.UInt32.FullyQualifiedName,
        WellKnownTypes.Int64.FullyQualifiedName, WellKnownTypes.UInt64.FullyQualifiedName,
        WellKnownTypes.Char.FullyQualifiedName, WellKnownTypes.Single.FullyQualifiedName,
        WellKnownTypes.Double.FullyQualifiedName, WellKnownTypes.Decimal.FullyQualifiedName,
    ];

    public static bool Supports(IMistType type)
    {
        return type switch
        {
            RefinedType refined => Supports(refined.BaseType),
            EnumType { UnderlyingType: { } underlying } => Supports(underlying),
            _ => Array.IndexOf(ComparableNames, type.FullyQualifiedName) >= 0,
        };
    }

    public static ComparisonPattern? Misordered(Pattern? predicate)
    {
        return predicate switch
        {
            ComparisonPattern { Kind: not ComparisonKind.Equal, NarrowedType: not ErrorType } comparison
                when !Supports(comparison.NarrowedType) => comparison,
            ConjunctionPattern conjunction => conjunction.Operands.Select(Misordered).FirstOrDefault(found => found is not null),
            DisjunctionPattern disjunction => disjunction.Operands.Select(Misordered).FirstOrDefault(found => found is not null),
            NegatedPattern negated => Misordered(negated.Operand),
            _ => null,
        };
    }

    public static bool IsRefused(IMistType type)
    {
        return type is RefinedType refined && Misordered(refined.Predicate) is not null;
    }
}