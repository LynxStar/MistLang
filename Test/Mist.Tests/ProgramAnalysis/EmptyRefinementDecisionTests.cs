using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class EmptyRefinementDecisionTests
{
    [Theory]
    [InlineData("int-crossed-bounds", true)]
    [InlineData("int-one-value", false)]
    [InlineData("byte-past-its-range", true)]
    [InlineData("byte-at-its-top", false)]
    [InlineData("refined-refinement-crossed", true)]
    [InlineData("refined-refinement-overlapping", false)]
    [InlineData("real-crossed-bounds", true)]
    [InlineData("real-narrow-range", false)]
    [InlineData("regex", false)]
    public void ARefinementIsEmptyOnlyWhenItsReadableValuesAre(string shape, bool empty)
    {
        var h = new HandBuiltRefinements();
        var octet = h.Built.Type("Byte");
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        (Pattern Predicate, IMistType Refines) input = shape switch
        {
            "int-crossed-bounds" => (h.Between(h.Int, 10L, 5L).Predicate!, h.Int),
            "int-one-value" => (h.Between(h.Int, 0L, 0L).Predicate!, h.Int),
            "byte-past-its-range" => (h.Compare(octet, ComparisonKind.GreaterThan, 300L), octet),
            "byte-at-its-top" => (h.Compare(octet, ComparisonKind.GreaterThanOrEqual, 255L), octet),
            "refined-refinement-crossed" => (h.Compare(positive, ComparisonKind.LessThan, 0L), positive),
            "refined-refinement-overlapping" => (h.Compare(positive, ComparisonKind.LessThan, 10L), positive),
            "real-crossed-bounds" => (h.Between(h.Double, 10.5, 2.0).Predicate!, h.Double),
            "real-narrow-range" => (h.Between(h.Double, 1.0, 1.5).Predicate!, h.Double),
            _ => (h.Regex("^a$").Predicate!, h.String),
        };

        Assert.Equal(empty, EmptyRefinements.AdmitsNothing(input.Predicate, input.Refines));
    }
}