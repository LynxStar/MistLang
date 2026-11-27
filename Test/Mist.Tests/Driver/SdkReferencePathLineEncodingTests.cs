using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class SdkReferencePathLineEncodingTests
{
    [Theory]
    [InlineData("/sdk/pack\rpreview/ref/net10.0", "net8.0\t/sdk/ref/net8.0")]
    [InlineData("/sdk/pack\npreview/ref/net10.0", "net8.0\t/sdk/ref/net8.0")]
    [InlineData("/sdk/pack/ref/net10.0", "net10.0\t/sdk/pack/ref/net10.0\nnet8.0\t/sdk/ref/net8.0")]
    public void DiscoveredPath_IsRemembered_AndOnlySingleLinePathsArePersisted(string discoveredPath, string expectedStore)
    {
        var store = "net8.0\t/sdk/ref/net8.0";
        var discoveries = new List<string>();
        var cache = new SdkReferencePathCache(
            (framework, _) =>
            {
                discoveries.Add(framework);
                return discoveredPath;
            },
            path => path == discoveredPath || path == "/sdk/ref/net8.0",
            () => store,
            revise => store = revise(store));

        Assert.Equal(discoveredPath, cache.Find("net10.0", new DiagnosticBag()));
        Assert.Equal(expectedStore, store);
        Assert.Equal(discoveredPath, cache.Find("net10.0", new DiagnosticBag()));
        Assert.Equal(["net10.0"], discoveries);
    }
}