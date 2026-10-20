using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// <see cref="Covered"/> refuses nothing C# would run, so an enum has no domain there: its arms are emitted
/// as equality guards, which C# never judges. <see cref="Admits"/> asks what values exist, so there an enum
/// is its variants.
/// </summary>
internal static class MatchedValues
{
    private static readonly Int128 UnsignedShift = (Int128)long.MinValue;

    public static bool Covered(IEnumerable<Pattern> earlier, Pattern later, IMistType input)
    {
        if(MatchedSpaces.Of(input) is not { } space || space.Read(later) is not { } reached || space.IsEmpty(reached))
        {
            return false;
        }

        var covered = space.Nothing;

        foreach(var pattern in earlier)
        {
            if(space.Read(pattern) is { } values && space.Or(covered, values) is { } joined)
            {
                covered = joined;
            }
        }

        return space.IsSubsetOf(reached, covered);
    }

    public static Admitted? Domain(IMistType input)
    {
        return IntegralDomain(input) ?? (Requirement.Unrefined(input) is EnumType enumType ? Variants(enumType) : null);
    }

    public static Admitted? Admits(Pattern pattern, IMistType input)
    {
        if(IntegralDomain(input) is { } domain)
        {
            return Integral(domain, input).Read(pattern);
        }

        if(Requirement.Unrefined(input) is not EnumType enumType)
        {
            return null;
        }

        var variants = Variants(enumType);

        return new ValueAlgebra<Admitted>(
            variants, Admitted.Nothing,
            (left, right) => left.And(right), (left, right) => left.Or(right), values => values.Not().And(variants),
            (values, other) => values.IsSubsetOf(other),
            (primary, self) => primary switch
            {
                ComparisonPattern comparison => Variant(enumType, comparison),
                TypeTestPattern test when SameDomain(test.NarrowedType, input) => Tested(test, self),
                _ => null,
            }).Read(pattern);
    }

    public static ValueAlgebra<object>? Scalar(IMistType input)
    {
        if(IntegralDomain(input) is { } domain)
        {
            return Integral(domain, input).Erased();
        }

        if(Requirement.Unrefined(input).FullyQualifiedName == WellKnownTypes.UInt64.FullyQualifiedName)
        {
            return Ordered(Admitted.Everything, input, comparison => Unsigned(comparison.Bound) is { } shifted
                ? Requirement.Interval(comparison.Kind, shifted)
                : null).Erased();
        }

        return RealRequirement.KindOf(input) is { } kind
            ? new ValueAlgebra<RealSet>(
                RealSet.Everything(kind), RealSet.Nothing(kind),
                (left, right) => left.And(right), (left, right) => left.Or(right), values => values.Not(),
                (values, other) => values.IsSubsetOf(other),
                (primary, self) => primary switch
                {
                    ComparisonPattern comparison when Orders(comparison, input) => RealRequirement.Compared(comparison.Kind, comparison.Bound, kind),
                    TypeTestPattern test when SameDomain(test.NarrowedType, input) => Tested(test, self),
                    _ => null,
                }).Erased()
            : null;
    }

    private static bool Orders(ComparisonPattern comparison, IMistType input)
    {
        return comparison.Kind == ComparisonKind.Equal || Comparability.Supports(Requirement.Unrefined(input));
    }

    internal static T? Tested<T>(TypeTestPattern test, ValueAlgebra<T> algebra)
        where T : class
    {
        if(Requirement.Refined(test.NarrowedType) is null)
        {
            return algebra.Everything;
        }

        return test.Refinement is { } predicate ? algebra.Read(predicate) : null;
    }

    private static bool SameDomain(IMistType tested, IMistType input)
    {
        return Requirement.Unrefined(tested).FullyQualifiedName == Requirement.Unrefined(input).FullyQualifiedName;
    }

    private static ValueAlgebra<Admitted> Integral(Admitted domain, IMistType input)
    {
        return Ordered(domain, input, comparison => Requirement.Constant(comparison.Bound) is { } constant
            ? Requirement.Interval(comparison.Kind, constant).And(domain)
            : null);
    }

    private static ValueAlgebra<Admitted> Ordered(Admitted domain, IMistType input, Func<ComparisonPattern, Admitted?> compared)
    {
        return new ValueAlgebra<Admitted>(
            domain, Admitted.Nothing,
            (left, right) => left.And(right), (left, right) => left.Or(right), values => values.Not().And(domain),
            (values, other) => values.IsSubsetOf(other),
            (primary, self) => primary switch
            {
                ComparisonPattern comparison when Orders(comparison, input) => compared(comparison),
                TypeTestPattern test when SameDomain(test.NarrowedType, input) => Tested(test, self),
                _ => null,
            });
    }

    private static long? Unsigned(Expression bound)
    {
        var value = bound is LiteralExpression { Value: char character } ? (Int128)character : IntegralConstant.Fold(bound);

        return value is { } constant && constant >= ulong.MinValue && constant <= ulong.MaxValue
            ? (long)(constant + UnsignedShift)
            : null;
    }

    private static Admitted Variants(EnumType enumType)
    {
        return Admitted.Between(0, enumType.MembersList.Count - 1);
    }

    private static Admitted? Variant(EnumType enumType, ComparisonPattern comparison)
    {
        if(comparison is not { Kind: ComparisonKind.Equal, Bound: MemberAccessExpression { Type: EnumType named } access }
            || named.FullyQualifiedName != enumType.FullyQualifiedName)
        {
            return null;
        }

        var index = enumType.MembersList.FindIndex(member => member.Name == access.MemberName);
        return index < 0 ? null : Admitted.Exactly(index);
    }

    private static Admitted? IntegralDomain(IMistType input)
    {
        var name = Requirement.Unrefined(input).FullyQualifiedName;

        return name == WellKnownTypes.Int32.FullyQualifiedName ? Admitted.Between(int.MinValue, int.MaxValue)
            : name == WellKnownTypes.Int64.FullyQualifiedName ? Admitted.Everything
            : name == WellKnownTypes.Int16.FullyQualifiedName ? Admitted.Between(short.MinValue, short.MaxValue)
            : name == WellKnownTypes.SByte.FullyQualifiedName ? Admitted.Between(sbyte.MinValue, sbyte.MaxValue)
            : name == WellKnownTypes.UInt32.FullyQualifiedName ? Admitted.Between(uint.MinValue, uint.MaxValue)
            : name == WellKnownTypes.UInt16.FullyQualifiedName ? Admitted.Between(ushort.MinValue, ushort.MaxValue)
            : name == WellKnownTypes.Byte.FullyQualifiedName ? Admitted.Between(byte.MinValue, byte.MaxValue)
            : name == WellKnownTypes.Char.FullyQualifiedName ? Admitted.Between(char.MinValue, char.MaxValue)
            : name == WellKnownTypes.Boolean.FullyQualifiedName ? Admitted.Between(0, 1)
            : null;
    }
}