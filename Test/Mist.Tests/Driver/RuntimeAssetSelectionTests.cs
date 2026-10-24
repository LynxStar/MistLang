using System.Collections.Immutable;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class RuntimeAssetSelectionTests
{
    private static readonly ImmutableArray<string> OsxArm64 = ["osx-arm64", "osx", "unix-arm64", "unix", "any", "base"];

    private static RuntimeAsset Native(string rid, string file = "libe_sqlite3.dylib")
    {
        return new RuntimeAsset($"runtimes/{rid}/native/{file}", rid, RuntimeAssetKind.Native);
    }

    private static RuntimeAsset Managed(string rid, string file = "Lib.dll")
    {
        return new RuntimeAsset($"runtimes/{rid}/lib/net8.0/{file}", rid, RuntimeAssetKind.Managed);
    }

    [Fact]
    public void TheSpecificIdentifier_WinsOverItsFallbacks()
    {
        var specific = Native("osx-arm64");

        var selected = RuntimeAssetSelection.Select([Native("osx"), specific, Native("unix"), Native("any")], OsxArm64);

        Assert.Equal<RuntimeAsset>([specific], selected);
    }

    [Fact]
    public void AFallback_IsUsedWhenTheSpecificIdentifierHasNothing()
    {
        var generic = Native("osx");

        var selected = RuntimeAssetSelection.Select([Native("linux-x64"), generic, Native("any")], OsxArm64);

        Assert.Equal<RuntimeAsset>([generic], selected);
    }

    [Fact]
    public void EveryFileOfTheChosenIdentifier_IsKeptInEntryOrder()
    {
        var first = Native("osx", "a.dylib");
        var second = Native("osx", "b.dylib");

        var selected = RuntimeAssetSelection.Select([first, Native("unix", "c.so"), second], OsxArm64);

        Assert.Equal<RuntimeAsset>([first, second], selected);
    }

    [Fact]
    public void NativeAndManagedAssets_AreChosenIndependently()
    {
        var native = Native("osx-arm64");
        var managed = Managed("any");

        var selected = RuntimeAssetSelection.Select(
            [Managed("win"), native, Native("any"), managed],
            OsxArm64);

        Assert.Equal<RuntimeAsset>([native, managed], selected);
    }

    [Fact]
    public void AnIdentifierAbsentFromTheOrder_IsIgnored()
    {
        var native = Native("osx-arm64");

        var selected = RuntimeAssetSelection.Select(
            [new RuntimeAsset("runtimes/browser-wasm/nativeassets/net9.0/e_sqlite3.a", "browser-wasm", RuntimeAssetKind.Native), native],
            OsxArm64);

        Assert.Equal<RuntimeAsset>([native], selected);
    }

    [Fact]
    public void APackageWithNothingForAnyIdentifierInTheOrder_YieldsNothing()
    {
        var selected = RuntimeAssetSelection.Select(
            [Native("linux-arm"), Native("win-x64"), Managed("browser-wasm")],
            OsxArm64);

        Assert.Empty(selected);
    }
}