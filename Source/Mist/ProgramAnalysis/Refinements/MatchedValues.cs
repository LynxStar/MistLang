using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// The domain is the input's unrefined type, as much as the emitted switch shows Roslyn, so nothing is refused
/// that C# would run. An enum has none, its arms emitted as equality guards C# never judges, nor a ulong, whose
/// range is not a set of longs; a refined type test over a refined base has no reading, emission testing only
/// the outermost predicate.
/// </summary>
internal static class MatchedValues
{
    public static bool Covered(IEnumerable<Pattern> earlier, Pattern later, IMistType input)
    {
        if(IntegralDomain(input) is { } domain)
        {
            return Covered(earlier, later, input, new Algebra<Admitted>(
                domain, Admitted.Nothing,
                (left, right) => left.And(right), (left, right) => left.Or(right), values => values.Not().And(domain),
                (values, other) => values.IsSubsetOf(other),
                comparison => Requirement.Of(comparison) is { Bounds.Length: 0 } requirement ? requirement.Values.And(domain) : null));
        }

        return RealRequirement.KindOf(input) is { } kind
            && Covered(earlier, later, input, new Algebra<RealSet>(
                RealSet.Everything(kind), RealSet.Nothing(kind),
                (left, right) => left.And(right), (left, right) => left.Or(right), values => values.Not(),
                (values, other) => values.IsSubsetOf(other),
                comparison => RealRequirement.Of(comparison, kind)));
    }

    private static bool Covered<T>(IEnumerable<Pattern> earlier, Pattern later, IMistType input, Algebra<T> algebra)
        where T : class
    {
        if(Read(later, input, algebra) is not { } reached || algebra.IsSubsetOf(reached, algebra.Nothing))
        {
            return false;
        }

        var covered = earlier.Aggregate(algebra.Nothing, (union, pattern) => Read(pattern, input, algebra) is { } values ? algebra.Or(union, values) : union);
        return algebra.IsSubsetOf(reached, covered);
    }

    private static T? Read<T>(Pattern pattern, IMistType input, Algebra<T> algebra)
        where T : class
    {
        return pattern switch
        {
            CatchAllPattern => algebra.Everything,
            ComparisonPattern comparison => algebra.Compared(comparison),
            NegatedPattern negated => Read(negated.Operand, input, algebra) is { } inner ? algebra.Not(inner) : null,
            ConjunctionPattern conjunction => Fold(conjunction.Operands, input, algebra, algebra.Everything, algebra.And),
            DisjunctionPattern disjunction => Fold(disjunction.Operands, input, algebra, algebra.Nothing, algebra.Or),
            TypeTestPattern test when SameDomain(test.NarrowedType, input) => Requirement.Refined(test.NarrowedType) switch
            {
                null => algebra.Everything,
                RefinedType { BaseType: var baseType } when Requirement.Refined(baseType) is null && test.Refinement is { } predicate
                    => Read(predicate, input, algebra),
                _ => null,
            },
            _ => null,
        };
    }

    private static T? Fold<T>(IEnumerable<Pattern> operands, IMistType input, Algebra<T> algebra, T seed, Func<T, T, T> combine)
        where T : class
    {
        var result = seed;

        foreach(var operand in operands)
        {
            if(Read(operand, input, algebra) is not { } part)
            {
                return null;
            }

            result = combine(result, part);
        }

        return result;
    }

    private static bool SameDomain(IMistType tested, IMistType input)
    {
        return Requirement.Unrefined(tested).FullyQualifiedName == Requirement.Unrefined(input).FullyQualifiedName;
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

    private sealed record Algebra<T>(
        T Everything, T Nothing, Func<T, T, T> And, Func<T, T, T> Or, Func<T, T> Not, Func<T, T, bool> IsSubsetOf, Func<ComparisonPattern, T?> Compared)
        where T : class;
}