using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RefinementContractRefusalTests
{
    [Theory]
    [InlineData("implementation-erases-nested-return-refinement")]
    [InlineData("implementation-getter-returns-weaker-refinement")]
    [InlineData("implementation-inherits-weaker-refined-return")]
    [InlineData("implementation-maps-default-to-unrefined-return")]
    [InlineData("implementation-maps-weaker-refined-return")]
    [InlineData("implementation-reference-return-narrows-storage")]
    [InlineData("implementation-replaces-default-with-unrefined-return")]
    [InlineData("implementation-returns-distinct-opaque-refinement")]
    [InlineData("implementation-returns-real-nan-admitting-domain")]
    [InlineData("implementation-returns-unrefined-negative-integral")]
    [InlineData("implementation-returns-unrefined-negative-real")]
    public void ReturnPromiseIsPreserved(string name)
    {
        var h = new HandBuiltRefinements();
        IMistType promised = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var produced = h.Int;
        Admitted? lifted = null;
        switch(name)
        {
            case "implementation-erases-nested-return-refinement":
                var box = HandBuiltModel.Class("Box<>");
                promised = h.Built.Interner.GetOrCreateConstructedType(box, [promised]);
                produced = h.Built.Interner.GetOrCreateConstructedType(box, [h.Int]);
                break;
            case "implementation-getter-returns-weaker-refinement":
            case "implementation-inherits-weaker-refined-return":
                produced = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, "NonNegative");
                break;
            case "implementation-reference-return-narrows-storage":
                produced = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 2L, "Strong");
                break;
            case "implementation-returns-distinct-opaque-refinement":
                promised = h.Regex("[a-z]+", "Letters");
                produced = h.Regex("[a-z]+", "SameLetters");
                break;
            case "implementation-returns-real-nan-admitting-domain":
                promised = h.Refine(h.Double, ComparisonKind.LessThanOrEqual, 0.0, "NonPositive");
                produced = HandBuiltRefinements.RefinePattern(h.Double, new NegatedPattern(HandBuiltModel.Node(), h.Double,
                    h.Compare(h.Double, ComparisonKind.GreaterThan, 0.0)), "NotPositive");
                break;
            case "implementation-returns-unrefined-negative-real":
                promised = h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, "Positive");
                produced = h.Double;
                break;
            default:
                lifted = Admitted.Exactly(-1);
                break;
        }

        var finding = RefinementContracts.ReturnFinding(produced, promised,
            name == "implementation-reference-return-narrows-storage", lifted);
        Assert.Equal("MIST_PA_REFINEMENT_RETURN_UNPROVEN", finding?.Number.Value);
    }

    [Theory]
    [InlineData("implementation-reference-parameter-erases-storage-refinement")]
    [InlineData("implementation-setter-strengthens-refinement")]
    [InlineData("implementation-strengthens-by-refined-type")]
    [InlineData("implementation-strengthens-constraint")]
    [InlineData("implementation-strengthens-symbolic-bound")]
    [InlineData("real-implementation-strengthens-constraint")]
    public void ParameterPromiseCannotBeStrengthened(string name)
    {
        var h = new HandBuiltRefinements();
        var promised = h.Int;
        IMistType demanded = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18L, "AdultAge");
        switch(name)
        {
            case "implementation-reference-parameter-erases-storage-refinement":
                promised = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
                demanded = h.Int;
                break;
            case "implementation-setter-strengthens-refinement":
                promised = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
                demanded = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 2L, "Strong");
                break;
            case "implementation-strengthens-constraint":
                promised = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, null);
                demanded = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 10L, null);
                break;
            case "implementation-strengthens-symbolic-bound":
                promised = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
                var field = new Field("Bound", h.Int, default, new MemberModifiers { IsReadonly = true });
                demanded = HandBuiltRefinements.RefinePattern(promised, h.Compare(h.Int, ComparisonKind.LessThan,
                    new FieldReferenceExpression(HandBuiltModel.Node(), field)), null);
                break;
            case "real-implementation-strengthens-constraint":
                promised = HandBuiltRefinements.RefinePattern(h.Double, new NegatedPattern(HandBuiltModel.Node(), h.Double,
                    h.Compare(h.Double, ComparisonKind.GreaterThan, 0.0)), null);
                demanded = h.Refine(h.Double, ComparisonKind.LessThanOrEqual, 0.0, null);
                break;
        }

        var finding = name == "implementation-setter-strengthens-refinement" ? RefinementContracts.SetterFinding(promised, demanded)
            : RefinementContracts.ParameterFinding(promised, demanded, name == "implementation-reference-parameter-erases-storage-refinement");
        Assert.Equal("MIST_PA_CONSTRAINT_STRENGTHENED", finding?.Number.Value);
    }

    [Theory]
    [InlineData("dependent-argument-violates-constraint", 0L, -1L)]
    [InlineData("dependent-argument-violates-an-extension-method-constraint", 3L, 1L)]
    public void DependentArgumentMustMeetItsDeclaredDomain(string name, long minimum, long argument)
    {
        _ = name;
        var h = new HandBuiltRefinements();
        var parameter = new TypeParameter("Size", 0, new Scope(null), null)
        {
            IsDependent = true,
            Domain = h.Built.Type("Int32"),
            Refinement = h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, minimum),
        };
        Assert.Equal("MIST_PA_DEPENDENT_ARGUMENT_VIOLATED", RefinementAnalyzer.DependentFinding(parameter, new LiftedType(h.Int, argument))?.Number.Value);
    }


    [Theory]
    [InlineData("real-narrowing-cast-has-no-conversion")]
    public void RealFlowCannotSupplyANarrowingConversion(string name)
    {
        _ = name;
        var h = new HandBuiltRefinements();
        var target = h.Built.Type("Single");
        var cast = new CastExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(h.Double), target, [], target);
        Assert.Equal("MIST_PA_CAST_HAS_NO_CONVERSION", CastAnalyzer.Judge(cast, h.Built.Interner, CloneConversionRefusals.None)?.Number.Value);
    }

}