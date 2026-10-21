using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal static class RefinementDecision
{
    public static DiagnosticDescriptor? Judge(
        Expression value, IMistType target, Admitted known, LiteralValue? literal = null, RealSet? real = null,
        bool onConstraint = false)
    {
        return Judge(value, target, known, literal, real, onConstraint, LiteralPredicates.MatchLimit);
    }

    internal static DiagnosticDescriptor? Judge(
        Expression value, IMistType target, Admitted known, LiteralValue? literal, RealSet? real, bool onConstraint, TimeSpan matchLimit)
    {
        if(value.Type is ErrorType)
        {
            return null;
        }

        var constituents = target is UnionType union ? union.Constituents : [target];
        var considered = constituents.Where(constituent => Requirement.Refined(constituent) is not null
            && (ConversionClassifier.Classify(Requirement.Unrefined(value.Type), Requirement.Unrefined(constituent))
                is ConversionKind.Identity or ConversionKind.Implicit || ConstantConversion.Fits(value, constituent))).ToList();

        if(considered.Count == 0 || considered.Any(constituent => Satisfied(value, constituent, known, literal, real, matchLimit)))
        {
            return null;
        }

        var first = considered[0];
        var constraint = onConstraint && Requirement.IsAnonymous(target);
        if(LiteralPredicates.Applies(first))
        {
            return LiteralPredicates.Decide(first, literal, matchLimit) == false
                ? constraint ? ProgramAnalysisDiagnostics.ConstraintViolated : ProgramAnalysisDiagnostics.RefinementViolated
                : constraint ? ProgramAnalysisDiagnostics.ConstraintUnproven : ProgramAnalysisDiagnostics.RefinementUnproven;
        }

        if(Requirement.OfType(first) is not { } required)
        {
            return constraint ? ProgramAnalysisDiagnostics.ConstraintUnproven : ProgramAnalysisDiagnostics.RefinementUnproven;
        }

        var only = Requirement.Constant(value) ?? Lengths.Exact(known);
        if(only is { } constant && !required.Values.Contains(constant))
        {
            return constraint ? ProgramAnalysisDiagnostics.ConstraintViolated : ProgramAnalysisDiagnostics.RefinementViolated;
        }

        return constraint ? ProgramAnalysisDiagnostics.ConstraintUnproven
            : Requirement.Refined(value.Type) is not null ? ProgramAnalysisDiagnostics.RefinementNotImplied
            : ProgramAnalysisDiagnostics.RefinementUnproven;
    }

    private static bool Satisfied(Expression value, IMistType target, Admitted known, LiteralValue? literal, RealSet? real, TimeSpan matchLimit)
    {
        // An unknown enum can hold a value outside the signed interval domain, even when the predicate covers that whole domain.
        if(known.IsEverything && Requirement.Unrefined(value.Type) is EnumType && Requirement.Refined(value.Type) is null)
        {
            return false;
        }

        if(LiteralPredicates.Applies(target))
        {
            return LiteralPredicates.Satisfied(target, value.Type, literal, value, matchLimit)
                || (LiteralPredicates.Decide(target, literal, matchLimit) is null && RealKnowledge.Satisfied(real, target));
        }

        return Requirement.OfType(target) is { } required
            ? required.Bounds.Length == 0 && known.IsSubsetOf(required.Values)
            : !Requirement.IsUnreadable(target) || ReferenceEquals(Requirement.Refined(value.Type), Requirement.Refined(target));
    }
}