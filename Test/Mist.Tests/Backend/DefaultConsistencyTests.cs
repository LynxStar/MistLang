// Tier 2 (Functional): purity Requirement 12.1 and defaultability Requirement 6 -- a field initializer of a
// DEFAULTABLE type and a default-block entry are pure scopes with no inputs, and a type with no default
// (an argument-taking constructor and no block) leaves its initializers as ordinary construction-time
// code. The purity rule judges each hand-built type's initializers and entries.

using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class DefaultConsistencyTests
{
    private const string NotConsistent = "MIST0143";

    [Theory]
    [InlineData("initializer-allocating-through-a-pure-constructor-of-a-pure-type")]
    [InlineData("initializer-allocating-through-a-pure-constructor")]
    [InlineData("initializer-allocating-through-an-inferred-pure-constructor")]
    [InlineData("initializer-reading-a-readonly-static")]
    [InlineData("initializer-adding-two-literals")]
    [InlineData("initializer-of-an-array-literal")]
    [InlineData("struct-initializer-reading-a-readonly-static")]
    [InlineData("default-entry-reading-a-readonly-static")]
    [InlineData("default-entry-allocating-through-a-pure-constructor")]
    public void AConsistentInitializerOrEntry_IsAdmitted(string declaration)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(declaration));
    }

    [Theory]
    [InlineData("initializer-reading-a-mutable-static", "N")]
    [InlineData("initializer-allocating-through-an-impure-constructor", "L")]
    [InlineData("struct-initializer-reading-a-mutable-static", "N")]
    [InlineData("property-initializer-reading-a-mutable-static", "N")]
    [InlineData("default-entry-reading-a-mutable-static", "N")]
    [InlineData("default-entry-allocating-through-an-impure-constructor", "L")]
    public void AnInconsistentInitializerOrEntry_IsRefusedNamingTheFieldAndTheType(string declaration, string field)
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze(declaration));

        Assert.Equal(NotConsistent, finding.Number.Value);
        Assert.Contains($"'{field}'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("Box", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("initializer-of-a-type-with-only-an-argument-taking-constructor")]
    [InlineData("impure-allocation-in-an-initializer-of-a-type-with-only-an-argument-taking-constructor")]
    [InlineData("struct-initializer-of-a-type-with-only-an-argument-taking-constructor")]
    [InlineData("static-initializer-reading-a-mutable-static")]
    public void ATypeWithNoDefault_LeavesItsInitializersUnconstrained(string declaration)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(declaration));
    }
}