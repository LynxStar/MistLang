using Mist.Backends.CSharpTranspiler.AmbientProvider;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class AmbientConstraintAvailabilityDecisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureSpreadsThroughDependenciesInEitherOrder(bool reverse)
    {
        var entries = new KeyValuePair<string, HashSet<string>>[]
        {
            new("A", ["B"]), new("B", ["C"]), new("C", ["Missing"]), new("Independent", []),
        };
        var graph = (reverse ? entries.Reverse() : entries).ToDictionary();

        var unavailable = AmbientTypeAvailability.Propagate(graph, ["C"]);

        Assert.True(unavailable.SetEquals(["A", "B", "C"]));
    }

    [Fact]
    public void CyclesNeedAProvenFailure()
    {
        var graph = new Dictionary<string, HashSet<string>>
        {
            ["Self"] = ["Self"],
            ["Left"] = ["Right"],
            ["Right"] = ["Left"],
        };

        Assert.Empty(AmbientTypeAvailability.Propagate(graph, []));
        Assert.True(AmbientTypeAvailability.Propagate(graph, ["Left"]).SetEquals(["Left", "Right"]));
    }
}