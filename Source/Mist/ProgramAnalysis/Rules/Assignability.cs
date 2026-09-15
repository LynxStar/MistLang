using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Whether a value may stand where a type is expected, decided once for every position that has one:
/// it may when the conversion is identity or implicit. Two things the classification alone would refuse
/// are admitted here: an integer constant that fits (<see cref="ConstantConversion"/>), and a target that
/// is a refinement over a type the value already belongs to, since whether the predicate holds is the
/// refinement prover's to discharge -- order 12 of the analysis register -- and a rule that judged it here
/// would refuse <c>AdultAge age = 20</c>, which classic admits. And a refinement refused at its own
/// declaration, one ordering a type with no order, admits any value: judging a value against it would be
/// a second complaint about that one mistake.
/// </summary>
internal static class Assignability
{
    public static DiagnosticDescriptor? Judge(Expression value, IMistType target, TypeInterner interner)
    {
        return Judge(value.Type, target, interner, ConstantConversion.IntegerConstant(value));
    }

    public static DiagnosticDescriptor? Judge(IMistType from, IMistType to, TypeInterner interner, long? integerConstant = null)
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