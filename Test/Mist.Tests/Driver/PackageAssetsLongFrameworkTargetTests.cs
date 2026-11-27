using System.Text.Json;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class PackageAssetsLongFrameworkTargetTests
{
    [Theory]
    [InlineData(".NETCoreApp,Version=v10.0")]
    [InlineData(".NETCoreApp,Version=v10.0/win-x64")]
    public void TargetSelectsTheLongFrameworkRowOverAnEarlierUnrelatedRow(string targetKey)
    {
        using var document = JsonDocument.Parse($$$"""
            {
              "targets": {
                "net8.0": {"Legacy.Package/1.0.0":{"type":"package"}},
                "{{{targetKey}}}": {"Current.Package/2.0.0":{"type":"package"}}
              }
            }
            """);

        var target = PackageAssets.Target(document.RootElement, "net10.0");

        Assert.Equal("""{"Current.Package/2.0.0":{"type":"package"}}""", target?.GetRawText());
    }
}