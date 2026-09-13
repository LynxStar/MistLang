using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The declared cast operators a cast from one type to another calls: one, several that tie, or none. A
/// cast is sugar for calling a conversion function (cast-operators spec, Requirement 8), so this is
/// overload resolution with the operand as the only argument. The candidates are the operators both ends
/// and their base chains declare whose result reaches the target losslessly; the operand reaches a
/// parameter as any argument does, which admits one user-declared implicit operator of its own; and two
/// equally good candidates are an ambiguity, never a choice. A lone candidate is returned unjudged, as a
/// call's is, leaving its argument to the rule that judges arguments. Among applicable candidates, one
/// answering exactly the written type is that type's own conversion and outranks one whose result only
/// widens into it -- without that, <c>(long)counter</c> would tie an <c>int</c> operator with a
/// <c>long</c> one. A cast the operand already satisfies losslessly calls nothing (Requirement 8.8).
/// <see cref="Implicit"/> is the same call written invisibly (Requirement 7): implicit operators only, every
/// candidate judged, and an operand reaching the parameter losslessly, being the one user-declared conversion
/// its value may pass through.
/// </summary>
internal static class CastOperatorSelection
{
    public static IReadOnlyList<CastOperator> Best(IMistType from, IMistType to, TypeInterner? interner, long? integerConstant = null)
    {
        if(ConversionClassifier.IsFree(from, to, interner))
        {
            return [];
        }

        from = ConversionClassifier.Normalized(from, interner);
        to = ConversionClassifier.Normalized(to, interner);

        var reaching = Reaching(from, to, interner, implicitOnly: false);

        if(reaching.Count < 2)
        {
            return reaching;
        }

        return Ranked(from, to, reaching.FindAll(
            castOperator => ConversionClassifier.IsImplicit(from, castOperator.SourceParameter.Type, interner)
                || Fits(integerConstant, from, castOperator)));
    }

    public static IReadOnlyList<CastOperator> Implicit(IMistType from, IMistType to, TypeInterner? interner, long? integerConstant = null)
    {
        if(ConversionClassifier.IsFree(from, to, interner)
            || (integerConstant is { } constant && ConstantConversion.Fits(constant, from, to)))
        {
            return [];
        }

        from = ConversionClassifier.Normalized(from, interner);
        to = ConversionClassifier.Normalized(to, interner);

        return Ranked(from, to, Reaching(from, to, interner, implicitOnly: true).FindAll(
            castOperator => ConversionClassifier.IsFree(from, castOperator.SourceParameter.Type, interner)
                || Fits(integerConstant, from, castOperator)));
    }

    private static List<CastOperator> Reaching(IMistType from, IMistType to, TypeInterner? interner, bool implicitOnly)
    {
        return ConversionRelation.DeclaredOn(from)
            .Concat(ConversionRelation.DeclaredOn(to))
            .Distinct()
            .Where(castOperator => (castOperator.IsImplicit || !implicitOnly)
                && ConversionClassifier.IsFree(castOperator.TargetType, to, interner))
            .ToList();
    }

    private static bool Fits(long? integerConstant, IMistType from, CastOperator castOperator)
    {
        return integerConstant is { } constant && ConstantConversion.Fits(constant, from, castOperator.SourceParameter.Type);
    }

    private static List<CastOperator> Ranked(IMistType from, IMistType to, List<CastOperator> applicable)
    {
        var exact = applicable.FindAll(castOperator => ConversionRelation.IsIdentity(castOperator.TargetType, to));
        var candidates = exact.Count > 0 ? exact : applicable;

        return candidates.FindAll(candidate => !candidates.Any(other => OverloadResolution.CompareConversions(
            from, other.SourceParameter.Type, candidate.SourceParameter.Type) < 0));
    }
}