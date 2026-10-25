using System.Collections.Immutable;
using System.Text.Json;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class PackageAssetsParsingTests
{
    private static readonly string Packages = OperatingSystem.IsWindows() ? @"C:\nuget" : "/nuget";

    private static readonly ImmutableArray<string> OsxArm64 = ["osx-arm64", "osx", "unix-arm64", "unix", "any", "base"];

    private const string Assets = """
        {
          "version": 3,
          "targets": {
            "net10.0": {
              "Bridge/1.0.0": {
                "type": "project",
                "runtimeTargets": {
                  "runtimes/osx-arm64/native/libbridge.dylib": { "assetType": "native", "rid": "osx-arm64" }
                }
              },
              "Some.Managed/1.0.0-Beta": {
                "type": "package",
                "runtimeTargets": {
                  "runtimes/unix/lib/net8.0/Some.Managed.dll": { "assetType": "runtime", "rid": "unix" },
                  "runtimes/win/lib/net8.0/Some.Managed.dll": { "assetType": "runtime", "rid": "win" }
                }
              },
              "SQLitePCLRaw.lib.e_sqlite3/2.1.12": {
                "type": "package",
                "compile": { "lib/netstandard2.0/_._": {} },
                "runtimeTargets": {
                  "runtimes/linux-arm/native/libe_sqlite3.so": { "assetType": "native", "rid": "linux-arm" },
                  "runtimes/osx-arm64/native/libe_sqlite3.dylib": { "assetType": "native", "rid": "osx-arm64" },
                  "runtimes/browser-wasm/nativeassets/net9.0/e_sqlite3.a": { "assetType": "native", "rid": "browser-wasm" }
                }
              }
            }
          },
          "project": {
            "frameworks": {
              "net10.0": {
                "runtimeIdentifierGraphPath": "/sdk/10.0.103/PortableRuntimeIdentifierGraph.json"
              }
            }
          }
        }
        """;

    private static string InPackage(string id, string version, params string[] path)
    {
        return Path.Combine([Packages, id, version, .. path]);
    }

    [Fact]
    public void TheGraphPath_IsReadFromTheProjectFramework()
    {
        using var document = JsonDocument.Parse(Assets);

        Assert.Equal(
            "/sdk/10.0.103/PortableRuntimeIdentifierGraph.json",
            PackageAssets.RuntimeIdentifierGraphPath(document.RootElement, "net10.0"));
    }

    [Fact]
    public void AnAssetsFileNamingNoGraph_HasNoGraphPath()
    {
        using var document = JsonDocument.Parse("""{ "project": { "frameworks": { "net10.0": {} } } }""");

        Assert.Null(PackageAssets.RuntimeIdentifierGraphPath(document.RootElement, "net10.0"));
    }

    [Fact]
    public void EachRuntimeTarget_BecomesAnAssetUnderThePackageDirectory()
    {
        using var document = JsonDocument.Parse(Assets);
        var library = document.RootElement.GetProperty("targets").GetProperty("net10.0").GetProperty("SQLitePCLRaw.lib.e_sqlite3/2.1.12");
        var directory = InPackage("sqlitepclraw.lib.e_sqlite3", "2.1.12");

        Assert.Equal<RuntimeAsset>(
            [
                new(InPackage("sqlitepclraw.lib.e_sqlite3", "2.1.12", "runtimes", "linux-arm", "native", "libe_sqlite3.so"), "linux-arm", RuntimeAssetKind.Native),
                new(InPackage("sqlitepclraw.lib.e_sqlite3", "2.1.12", "runtimes", "osx-arm64", "native", "libe_sqlite3.dylib"), "osx-arm64", RuntimeAssetKind.Native),
                new(InPackage("sqlitepclraw.lib.e_sqlite3", "2.1.12", "runtimes", "browser-wasm", "nativeassets", "net9.0", "e_sqlite3.a"), "browser-wasm", RuntimeAssetKind.Native),
            ],
            PackageAssets.RuntimeTargets(library, directory));
    }

    [Fact]
    public void ARuntimeAssetType_IsManaged_AndOtherAssetTypesOrEntriesWithoutARidAreSkipped()
    {
        using var document = JsonDocument.Parse("""
            {
              "runtimeTargets": {
                "runtimes/win/lib/net8.0/A.dll": { "assetType": "runtime", "rid": "win" },
                "runtimes/win/lib/net8.0/de/A.resources.dll": { "assetType": "resource", "rid": "win" },
                "runtimes/win/native/a.dll": { "assetType": "native" }
              }
            }
            """);

        var asset = Assert.Single(PackageAssets.RuntimeTargets(document.RootElement, Packages));

        Assert.Equal(new RuntimeAsset(Path.Combine(Packages, "runtimes", "win", "lib", "net8.0", "A.dll"), "win", RuntimeAssetKind.Managed), asset);
    }

    [Fact]
    public void ALibraryWithoutRuntimeTargets_HasNone()
    {
        using var document = JsonDocument.Parse("""{ "type": "package", "compile": { "lib/net8.0/A.dll": {} } }""");

        Assert.Empty(PackageAssets.RuntimeTargets(document.RootElement, Packages));
    }

    [Fact]
    public void Selection_KeepsEachPackagesBestAssets_InPackageOrder_AndSkipsProjects()
    {
        using var document = JsonDocument.Parse(Assets);

        var selected = PackageAssets.SelectRuntimeAssets(document.RootElement, "net10.0", Packages, OsxArm64);

        Assert.Equal<RuntimeAsset>(
            [
                new(InPackage("some.managed", "1.0.0-beta", "runtimes", "unix", "lib", "net8.0", "Some.Managed.dll"), "unix", RuntimeAssetKind.Managed),
                new(InPackage("sqlitepclraw.lib.e_sqlite3", "2.1.12", "runtimes", "osx-arm64", "native", "libe_sqlite3.dylib"), "osx-arm64", RuntimeAssetKind.Native),
            ],
            selected);
    }

    [Fact]
    public void Selection_ReadsTheTargetOfTheProjectFramework()
    {
        using var document = JsonDocument.Parse("""
            {
              "targets": {
                "net8.0": { "A/1.0.0": { "runtimeTargets": { "runtimes/osx/native/old.dylib": { "assetType": "native", "rid": "osx" } } } },
                "net10.0": { "A/1.0.0": { "runtimeTargets": { "runtimes/osx/native/new.dylib": { "assetType": "native", "rid": "osx" } } } }
              }
            }
            """);

        var asset = Assert.Single(PackageAssets.SelectRuntimeAssets(document.RootElement, "net10.0", Packages, OsxArm64));

        Assert.Equal(InPackage("a", "1.0.0", "runtimes", "osx", "native", "new.dylib"), asset.Path);
    }
}