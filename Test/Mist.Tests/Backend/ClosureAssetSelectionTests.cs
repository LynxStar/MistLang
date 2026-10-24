using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ClosureAssetSelectionTests
{
    private static readonly ImmutableArray<string> OsxArm64 = ["osx-arm64", "osx", "unix-arm64", "unix", "any", "base"];

    [Fact]
    public void TheSpecificIdentifierWinsAndEveryOneOfItsFilesIsSelected()
    {
        var selected = ClosureAssetSelection.Select(
            ["runtimes/osx/native/libgeneric.dylib", "runtimes/osx-arm64/native/libe_sqlite3.dylib", "runtimes/osx-arm64/native/libextra.dylib"],
            OsxArm64);

        Assert.Equal(
            [
                new RuntimeAsset("runtimes/osx-arm64/native/libe_sqlite3.dylib", "osx-arm64", RuntimeAssetKind.Native),
                new RuntimeAsset("runtimes/osx-arm64/native/libextra.dylib", "osx-arm64", RuntimeAssetKind.Native),
            ],
            selected.AsEnumerable());
    }

    [Fact]
    public void AFallbackIdentifierIsUsedWhenTheSpecificOneHasNoFiles()
    {
        var selected = ClosureAssetSelection.Select(
            ["runtimes/linux-x64/native/libe_sqlite3.so", "runtimes/osx/native/libe_sqlite3.dylib", "runtimes/unix/native/libunix.so"],
            OsxArm64);

        Assert.Equal(["runtimes/osx/native/libe_sqlite3.dylib"], selected.Select(asset => asset.Path));
    }

    [Fact]
    public void NativeAndManagedAssetsChooseTheirIdentifiersIndependently()
    {
        var selected = ClosureAssetSelection.Select(
            ["runtimes/osx/native/libother.dylib", "runtimes/osx-arm64/native/libnative.dylib", "runtimes/unix/lib/net8.0/Managed.dll"],
            OsxArm64);

        Assert.Equal(
            [
                new RuntimeAsset("runtimes/unix/lib/net8.0/Managed.dll", "unix", RuntimeAssetKind.Managed),
                new RuntimeAsset("runtimes/osx-arm64/native/libnative.dylib", "osx-arm64", RuntimeAssetKind.Native),
            ],
            selected.AsEnumerable());
    }

    [Fact]
    public void AnIdentifierOutsideTheOrderIsNeverSelected()
    {
        var selected = ClosureAssetSelection.Select(
            ["runtimes/win-x64/native/e_sqlite3.dll", "runtimes/linux-arm/lib/net8.0/Managed.dll"], OsxArm64);

        Assert.Empty(selected);
    }

    [Fact]
    public void NothingOutsideTheNativeAndLibFrameworkFoldersIsSelected()
    {
        var selected = ClosureAssetSelection.Select(
            [
                "App.Migrations.dll",
                "de/App.Migrations.resources.dll",
                "native/osx/libmisplaced.dylib",
                "runtimes/osx/libstray.dylib",
                "runtimes/osx/lib/Untargeted.dll",
                "runtimes/osx/other/net8.0/Other.dll",
                "runtimes/browser-wasm/nativeassets/net9.0/e_sqlite3.a",
            ],
            ["osx-arm64", "osx", "browser-wasm", "any"]);

        Assert.Empty(selected);
    }

    [Fact]
    public void AFileNestedBelowNativeIsNotAnAssetAndDoesNotClaimItsIdentifier()
    {
        var selected = ClosureAssetSelection.Select(
            ["runtimes/osx-arm64/native/nested/libdeep.dylib", "runtimes/osx/native/libe_sqlite3.dylib"], OsxArm64);

        Assert.Equal(["runtimes/osx/native/libe_sqlite3.dylib"], selected.Select(asset => asset.Path));
    }

    [Fact]
    public void AListingWithBackslashSeparatorsIsReadTheSame()
    {
        var selected = ClosureAssetSelection.Select(
            ["runtimes\\osx\\native\\libgeneric.dylib", "runtimes\\osx-arm64\\native\\libe_sqlite3.dylib"], OsxArm64);

        Assert.Equal(
            [new RuntimeAsset("runtimes\\osx-arm64\\native\\libe_sqlite3.dylib", "osx-arm64", RuntimeAssetKind.Native)],
            selected.AsEnumerable());
    }

    [Fact]
    public void WithoutAnIdentifierOrderNothingIsSelected()
    {
        Assert.Empty(ClosureAssetSelection.Select(["runtimes/osx-arm64/native/libe_sqlite3.dylib"], []));
        Assert.Empty(ClosureAssetSelection.Select(["runtimes/osx-arm64/native/libe_sqlite3.dylib"], default));
    }
}