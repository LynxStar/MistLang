using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Refinements;

internal static class EmptyRefinements
{
    // A bound that names another value only narrows what the constant bounds already admit, so an empty
    // constant reading is empty whatever those bounds are; a predicate this cannot read is never called empty.
    public static bool AdmitsNothing(Pattern predicate, IMistType refines)
    {
        if(RealRequirement.KindOf(refines) is { } kind)
        {
            return RealRequirement.Of(predicate, kind) is { } own
                && (RealRequirement.OfType(refines) is { } beneath ? own.And(beneath) : own).IsSubsetOf(RealSet.Nothing(kind));
        }

        if(Requirement.Of(predicate) is not { } requirement)
        {
            return false;
        }

        var values = requirement.Values;
        if(Requirement.OfType(refines) is { } underneath)
        {
            values = values.And(underneath.Values);
        }

        if(IntegralTypes.Range(refines) is { } range)
        {
            values = values.And(Admitted.Between(Clamp(range.Low, long.MinValue), Clamp(range.High, long.MaxValue)));
        }

        return values.Intervals.IsEmpty;
    }

    private static long Clamp(BigInteger? end, long open)
    {
        return end is not { } value ? open : (long)BigInteger.Min(BigInteger.Max(value, long.MinValue), long.MaxValue);
    }
}