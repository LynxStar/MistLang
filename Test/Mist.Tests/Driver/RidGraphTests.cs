using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class RidGraphTests
{
    private const string SdkShapedGraph = """
        {
          "runtimes": {
            "any": { "#import": [ "base" ] },
            "base": { "#import": [] },
            "osx": { "#import": [ "unix" ] },
            "osx-arm64": { "#import": [ "osx", "unix-arm64" ] },
            "unix": { "#import": [ "any" ] },
            "unix-arm64": { "#import": [ "unix" ] },
            "win": { "#import": [ "any" ] },
            "win-x64": { "#import": [ "win" ] }
          }
        }
        """;

    private static RidGraph Graph()
    {
        return RidGraph.Parse(SdkShapedGraph)!;
    }

    [Fact]
    public void TheOrder_WalksImportsBreadthFirst_EachOnce_TargetFirst()
    {
        Assert.Equal<string>(
            ["osx-arm64", "osx", "unix-arm64", "unix", "any", "base"],
            RidGraph.FallbackOrder(Graph(), "osx-arm64"));
    }

    [Fact]
    public void AnIdentifierInTheGraph_IsContained()
    {
        Assert.True(RidGraph.Contains(Graph(), "win-x64"));
    }

    [Fact]
    public void AnIdentifierAbsentFromTheGraph_IsNotContained_AndFallsBackToNothingButItself()
    {
        var graph = Graph();

        Assert.False(RidGraph.Contains(graph, "linux-x64"));
        Assert.Equal<string>(["linux-x64"], RidGraph.FallbackOrder(graph, "linux-x64"));
    }

    [Fact]
    public void AnImportTheGraphDoesNotList_EndsItsBranch()
    {
        var graph = RidGraph.Parse("""{ "runtimes": { "a-x64": { "#import": [ "a", "missing" ] }, "a": { "#import": [] } } }""")!;

        Assert.Equal<string>(["a-x64", "a", "missing"], RidGraph.FallbackOrder(graph, "a-x64"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "other": {} }""")]
    [InlineData("""{ "runtimes": {} }""")]
    [InlineData("[]")]
    public void ADocumentWithoutRuntimes_IsNoGraph(string json)
    {
        Assert.Null(RidGraph.Parse(json));
    }

    [Theory]
    [InlineData("osx-arm64", new[] { "osx-arm64", "osx", "unix", "any" })]
    [InlineData("osx-x64", new[] { "osx-x64", "osx", "unix", "any" })]
    [InlineData("linux-x64", new[] { "linux-x64", "linux", "unix", "any" })]
    [InlineData("linux-arm64", new[] { "linux-arm64", "linux", "unix", "any" })]
    [InlineData("win-x64", new[] { "win-x64", "win", "any" })]
    [InlineData("win-arm64", new[] { "win-arm64", "win", "any" })]
    public void TheBuiltInTable_CoversTheCommonIdentifiers(string rid, string[] expected)
    {
        Assert.Equal(expected, RidGraph.FallbackOrder(RidGraph.BuiltIn, rid));
    }

    [Theory]
    [InlineData("linux-musl-x64", new[] { "linux-musl-x64", "linux-musl", "linux", "unix", "any" })]
    [InlineData("freebsd-x64", new[] { "freebsd-x64", "freebsd", "any" })]
    [InlineData("browser", new[] { "browser", "any" })]
    public void TheBuiltInTable_DerivesAnUnlistedIdentifierBySegments(string rid, string[] expected)
    {
        Assert.Equal(expected, RidGraph.FallbackOrder(RidGraph.BuiltIn, rid));
    }

    [Fact]
    public void TheBuiltInTable_AcceptsEveryIdentifier()
    {
        Assert.True(RidGraph.Contains(RidGraph.BuiltIn, "foo-bar"));
    }
}