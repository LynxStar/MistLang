// Tier 2 (Functional): an `owned` return type is a promise of provenance -- the caller receives the only
// reference -- so a body may return only what it owns: a fresh allocation, an owned parameter or binding,
// or a value that cannot change. Returning this, an unrestricted parameter or a static would hand out an
// alias of something another holder still reaches, and is refused by the ownership rule over a
// hand-built body.

using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class OwnedReturnProvenanceTests
{
    private const string NotOwned = "MIST_PA_OWNED_RETURN_NOT_OWNED";

    [Theory]
    [InlineData("owned-return-of-this")]
    [InlineData("owned-return-of-an-unrestricted-parameter")]
    [InlineData("owned-return-of-a-static-field")]
    public void AnOwnedReturnOfWhatTheBodyDoesNotOwn_IsRefused(string member)
    {
        Assert.Equal(NotOwned, Assert.Single(new PuritySpecFragments().Analyze(member)).Id.Value);
    }

    [Theory]
    [InlineData("owned-return-of-a-fresh-allocation")]
    [InlineData("owned-return-of-an-owned-local")]
    [InlineData("owned-return-of-an-owned-parameter")]
    [InlineData("owned-return-of-a-string-literal")]
    [InlineData("owned-return-of-an-integer-literal")]
    public void AnOwnedReturnOfWhatTheBodyOwnsOrCannotChange_IsAdmitted(string member)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(member));
    }
}