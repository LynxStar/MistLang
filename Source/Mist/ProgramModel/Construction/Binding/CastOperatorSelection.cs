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
/// its value may pass through. Lossless admits one of Standard's intrinsic implicit conversions between free
/// steps, since the cast-operators glossary counts those as changing no data even where, as Regex to String,
/// no containment holds.
/// </summary>
internal static class CastOperatorSelection
{
    public static IReadOnlyList<ConversionFunction> Best(IMistType from, IMistType to, TypeInterner? interner, long? integerConstant = null)
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

        return Ranked(from, to, reaching.FindAll(function => ReachesAsArgument(from, function.Parameter, interner, integerConstant)));
    }

    public static bool ReachesAsArgument(IMistType from, IMistType parameter, TypeInterner? interner, long? integerConstant = null)
    {
        return ConversionClassifier.IsImplicit(from, parameter, interner)
            || (integerConstant is { } constant && ConstantConversion.Fits(constant, from, parameter))
            || Implicit(from, parameter, interner, integerConstant).Count > 0;
    }

    public static IReadOnlyList<ConversionFunction> Implicit(IMistType from, IMistType to, TypeInterner? interner, long? integerConstant = null)
    {
        if(ConversionClassifier.IsFree(from, to, interner)
            || (integerConstant is { } constant && ConstantConversion.Fits(constant, from, to)))
        {
            return [];
        }

        from = ConversionClassifier.Normalized(from, interner);
        to = ConversionClassifier.Normalized(to, interner);

        return Ranked(from, to, Reaching(from, to, interner, implicitOnly: true).FindAll(
            function => IsLossless(from, function.Parameter, interner) || Fits(integerConstant, from, function)));
    }

    private static List<ConversionFunction> Reaching(IMistType from, IMistType to, TypeInterner? interner, bool implicitOnly)
    {
        return ConversionRelation.DeclaredOn(from)
            .Concat(ConversionRelation.DeclaredOn(to))
            .DistinctBy(function => (function.Operator, function.Parameter.FullyQualifiedName, function.Result.FullyQualifiedName))
            .Where(function => (function.Operator.IsImplicit || !implicitOnly) && IsLossless(function.Result, to, interner))
            .ToList();
    }

    private static bool IsLossless(IMistType from, IMistType to, TypeInterner? interner)
    {
        return ConversionClassifier.IsFree(from, to, interner)
            || ConversionRelation.DeclaredOn(from).Concat(ConversionRelation.DeclaredOn(to)).Any(
                function => function.Operator is { IsImplicit: true, Modifiers.IsIntrinsic: true }
                    && ConversionClassifier.IsFree(from, function.Parameter, interner)
                    && ConversionClassifier.IsFree(function.Result, to, interner));
    }

    private static bool Fits(long? integerConstant, IMistType from, ConversionFunction function)
    {
        return integerConstant is { } constant && ConstantConversion.Fits(constant, from, function.Parameter);
    }

    private static List<ConversionFunction> Ranked(IMistType from, IMistType to, List<ConversionFunction> applicable)
    {
        var exact = applicable.FindAll(function => ConversionRelation.IsIdentity(function.Result, to));
        var candidates = exact.Count > 0 ? exact : applicable;

        return candidates.FindAll(candidate => !candidates.Any(other => OverloadResolution.CompareConversions(
            from, other.Parameter, candidate.Parameter) < 0));
    }
}