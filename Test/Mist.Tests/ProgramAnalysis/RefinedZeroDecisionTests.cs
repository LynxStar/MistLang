using System.Text.RegularExpressions;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RefinedZeroDecisionTests
{
    private readonly HandBuiltRefinements refine = new();

    public RefinedZeroDecisionTests()
    {
        foreach(var name in new[] { "Int32", "Double", "String" })
        {
            refine.Built.Type(name).Modifiers = new MemberModifiers { IsIntrinsic = true };
        }
    }

    [Theory]
    [InlineData("non-negative", true)]
    [InlineData("positive", false)]
    [InlineData("negative", false)]
    [InlineData("around-zero", true)]
    [InlineData("not-zero", false)]
    [InlineData("small-positive", false)]
    [InlineData("small-non-negative", true)]
    [InlineData("non-negative-real", true)]
    [InlineData("positive-real", false)]
    [InlineData("any-text", true)]
    [InlineData("non-empty-text", false)]
    [InlineData("refined-class", false)]
    [InlineData("unrefined", true)]
    public void BasesZero_IsAdmittedOnlyWhereThePredicateProvesIt(string shape, bool admitted)
    {
        Assert.Equal(admitted, RefinedZero.IsAdmitted(Shaped(shape), Regex.InfiniteMatchTimeout));
    }

    private IMistType Shaped(string shape)
    {
        var integer = refine.Int;
        return shape switch
        {
            "non-negative" => refine.Refine(integer, ComparisonKind.GreaterThanOrEqual, 0L),
            "positive" => refine.Refine(integer, ComparisonKind.GreaterThan, 0L),
            "negative" => refine.Refine(integer, ComparisonKind.LessThan, 0L),
            "around-zero" => refine.Between(integer, -1L, 1L),
            "not-zero" => HandBuiltRefinements.RefinePattern(integer,
                new NegatedPattern(HandBuiltModel.Node(), integer, refine.Compare(integer, ComparisonKind.Equal, 0L))),
            "small-positive" => refine.Refine(refine.Refine(integer, ComparisonKind.GreaterThan, 0L), ComparisonKind.LessThan, 10L),
            "small-non-negative" => refine.Refine(refine.Refine(integer, ComparisonKind.GreaterThanOrEqual, 0L), ComparisonKind.LessThanOrEqual, 10L),
            "non-negative-real" => refine.Refine(refine.Double, ComparisonKind.GreaterThanOrEqual, 0.0),
            "positive-real" => refine.Refine(refine.Double, ComparisonKind.GreaterThan, 0.0),
            "any-text" => refine.Regex(".*"),
            "non-empty-text" => refine.Regex(".+"),
            "refined-class" => refine.Refine(HandBuiltModel.Class("Meter"), ComparisonKind.GreaterThanOrEqual, 0L),
            _ => integer,
        };
    }
}