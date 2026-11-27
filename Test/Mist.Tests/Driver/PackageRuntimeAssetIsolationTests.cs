using System.Text.Json;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "2")]
public sealed class PackageRuntimeAssetIsolationTests
{
    [Fact]
    public void EachPackageSelectsItsOwnBestNativeRid()
    {
        using var document = JsonDocument.Parse("""
            {
              "targets": {
                "net10.0": {
                  "Native.Exact/1.0.0": {
                    "type": "package",
                    "runtimeTargets": {
                      "runtimes/osx/native/libexact.dylib": { "assetType": "native", "rid": "osx" },
                      "runtimes/osx-arm64/native/libexact.dylib": { "assetType": "native", "rid": "osx-arm64" }
                    }
                  },
                  "Native.Fallback/2.0.0": {
                    "type": "package",
                    "runtimeTargets": {
                      "runtimes/osx/native/libfallback.dylib": { "assetType": "native", "rid": "osx" }
                    }
                  }
                }
              }
            }
            """);
        var packages = OperatingSystem.IsWindows() ? @"C:\synthetic-packages" : "/synthetic-packages";

        var selected = PackageAssets.SelectRuntimeAssets(document.RootElement, "net10.0", [packages], ["osx-arm64", "osx"]);

        Assert.Equal<RuntimeAsset>(
            [
                new(Path.Combine(packages, "native.exact", "1.0.0", "runtimes", "osx-arm64", "native", "libexact.dylib"), "osx-arm64", RuntimeAssetKind.Native),
                new(Path.Combine(packages, "native.fallback", "2.0.0", "runtimes", "osx", "native", "libfallback.dylib"), "osx", RuntimeAssetKind.Native),
            ],
            selected);
    }
}