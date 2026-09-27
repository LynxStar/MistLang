// Tier 2 (Functional): each purity reporting site keeps its node -- the offending expression, a drop's declaration, a chain's constructor, a closing, a conforming type, a default entry and a subpattern.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PuritySiteTests
{
    [Theory]
    [InlineData("pure-method-writes-a-static")]
    [InlineData("pure-method-calls-an-unmarked-method")]
    [InlineData("pure-method-stores-a-frozen-reference")]
    [InlineData("pure-lambda-writes-a-captured-local")]
    [InlineData("frozen-method-group-to-pure-func")]
    [InlineData("pure-method-holds-a-linear-with-an-impure-drop")]
    [InlineData("pure-constructor-chains-to-an-unmarked-one")]
    [InlineData("generic-type-test-instantiated-with-a-pure-function-type")]
    [InlineData("pure-abstract-member-implemented-impurely")]
    [InlineData("owned-slot-answered-by-an-unowned-return")]
    [InlineData("default-entry-not-consistent")]
    [InlineData("readonly-method-reads-a-mutable-getter-in-a-property-pattern")]
    [InlineData("static-initializer-lambda-converting-to-a-pure-function-type")]
    public void RefusalPointsAtTheOperationDeclarationClosingConformerEntryOrSubpattern(string site)
    {
        var diagnostic = Assert.Single(new PurityFragments().Analyze(site));

        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }
}