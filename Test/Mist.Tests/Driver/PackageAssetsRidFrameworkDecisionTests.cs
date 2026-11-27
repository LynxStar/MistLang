using System.Text.Json;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class PackageAssetsRidFrameworkDecisionTests
{
    [Fact]
    public void GraphPathComesFromTheSelectedFramework()
    {
        using var document = JsonDocument.Parse("""
            {
              "project": {
                "frameworks": {
                  "net8.0": { "runtimeIdentifierGraphPath": "/sdk/8.0.100/PortableRuntimeIdentifierGraph.json" },
                  "net10.0": { "runtimeIdentifierGraphPath": "/sdk/10.0.103/PortableRuntimeIdentifierGraph.json" }
                }
              }
            }
            """);

        Assert.Equal(
            "/sdk/10.0.103/PortableRuntimeIdentifierGraph.json",
            PackageAssets.RuntimeIdentifierGraphPath(document.RootElement, "net10.0"));
    }

    [Fact]
    public void SelectedFrameworkWithoutAGraphHasNoGraphPath()
    {
        using var document = JsonDocument.Parse("""
            {
              "project": {
                "frameworks": {
                  "net8.0": { "runtimeIdentifierGraphPath": "/sdk/8.0.100/PortableRuntimeIdentifierGraph.json" },
                  "net10.0": {}
                }
              }
            }
            """);

        Assert.Null(PackageAssets.RuntimeIdentifierGraphPath(document.RootElement, "net10.0"));
    }
}