using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// <see cref="ConstantConversion"/> admits fitting integer constants beyond ordinary implicit conversions.
/// Refinement predicates belong to the refinement prover; this decision checks their base conversion only
/// and admits values against a predicate refused at its declaration to avoid a second complaint.
/// </summary>
internal static class Assignability
{
    public static MistDiagnostic? Judge(Expression value, IMistType target, TypeInterner interner)
    {
        return Judge(value.Type, target, interner, ConstantConversion.IntegerConstant(value));
    }

    public static MistDiagnostic? Judge(IMistType from, IMistType to, TypeInterner interner, Int128? integerConstant = null)
    {
        var kind = ConversionClassifier.Classify(from, to, interner);

        if(kind is ConversionKind.Identity or ConversionKind.Implicit or ConversionKind.Unclassified
            || AwaitsRefinementProof(from, to, interner))
        {
            return null;
        }

        if(kind == ConversionKind.Explicit)
        {
            return ProgramAnalysisDiagnostics.CastRequired;
        }

        return integerConstant is { } constant && ConstantConversion.Fits(constant, from, to)
            ? null
            : ProgramAnalysisDiagnostics.NoImplicitConversion
            ;
    }

    /// <summary>
    /// A ref struct converted to what is not one -- an interface it implements, or the top type -- only by
    /// boxing, which ref-structs Requirement 3.11 refuses where the conversion happens. Kept apart from
    /// <see cref="Judge(IMistType, IMistType, TypeInterner, Int128?)"/>, which answers whether a conversion
    /// exists at all and is asked that by overload pairing too. A union is left alone: one holding a ref
    /// struct is refused where it is written.
    /// </summary>
    public static MistDiagnostic? Boxing(IMistType from, IMistType to, TypeInterner interner)
    {
        return RefStructs.Is(from)
            && !RefStructs.Is(to)
            && Unrefined(to) is not (UnionType or TypeParameter)
            && ConversionClassifier.Classify(from, to, interner) == ConversionKind.Implicit
                ? ProgramAnalysisDiagnostics.RefStructBoxed
                : null;
    }

    public static MistDiagnostic? JudgeSelection(int implicitOperatorCount)
    {
        return implicitOperatorCount > 1 ? ProgramAnalysisDiagnostics.AmbiguousImplicitConversion : null;
    }

    private static bool AwaitsRefinementProof(IMistType from, IMistType to, TypeInterner interner)
    {
        var constituents = to is UnionType union ? union.Constituents : [to];

        return constituents.Any(constituent => Comparability.IsRefused(constituent) || (constituent is RefinedType refined
            && ConversionClassifier.Classify(from, Unrefined(refined), interner) is ConversionKind.Identity or ConversionKind.Implicit));
    }

    private static IMistType Unrefined(IMistType type)
    {
        return type is RefinedType refined ? Unrefined(refined.BaseType) : type;
    }
}