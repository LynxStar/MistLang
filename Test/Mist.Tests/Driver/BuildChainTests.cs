using System.Collections.Immutable;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class BuildChainTests
{
    private static readonly string Root = Path.Combine("/work", "App", "mist.toml");
    private static readonly string Library = Path.Combine("/work", "App.Data", "mist.toml");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentVariable_IsAnEmptyChain(string? value)
    {
        Assert.Empty(BuildChain.Parse(value));
    }

    [Fact]
    public void AnExtendedChain_RoundTripsThroughTheVariable()
    {
        var chain = BuildChain.Extend(BuildChain.Extend([], Root), Library);

        Assert.Equal<string>([Root, Library], BuildChain.Parse(BuildChain.Format(chain)));
    }

    [Fact]
    public void AManifestNotInTheChain_IsNoCycle()
    {
        ImmutableArray<string> chain = [Root];

        Assert.False(BuildChain.Contains(chain, Library));
        Assert.Null(BuildChain.CycleThrough(chain, Library));
    }

    [Fact]
    public void AManifestAlreadyInTheChain_IsACycle_NamingTheWholeChain()
    {
        ImmutableArray<string> chain = [Root, Library];

        Assert.Equal($"{Root} -> {Library} -> {Root}", BuildChain.CycleThrough(chain, Root));
    }

    [Fact]
    public void AManifestDifferingOnlyInCase_IsTheSameManifest()
    {
        Assert.True(BuildChain.Contains([Root.ToUpperInvariant()], Root));
    }
}