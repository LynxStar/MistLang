using Mist.Driver;

namespace Mist.Tests.Driver;

// Reference-pack selection over a hand-built pack layout in one private temp directory per test.
[Trait("Tier", "2")]
public sealed class SdkReferencePackSelectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"mist_ref_packs_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch(IOException)
        {
        }
    }

    private string AddRefFolder(string pack, string version, string tfm)
    {
        return Directory.CreateDirectory(Path.Combine(root, "packs", pack, version, "ref", tfm)).FullName;
    }

    private string AddCorePack(string version, string tfm)
    {
        return AddRefFolder("Microsoft.NETCore.App.Ref", version, tfm);
    }

    [Fact]
    public void WithoutTheCorePackFolder_NothingIsFound()
    {
        _ = AddRefFolder("Microsoft.AspNetCore.App.Ref", "10.0.1", "net10.0");

        Assert.Null(SdkDiscovery.FindHighestVersionRefPath(root, "net10.0"));
    }

    [Fact]
    public void APackOfAnotherMajor_IsPassedOver_EvenWhenItHoldsTheTfmFolder()
    {
        var expected = AddCorePack("10.0.5", "net10.0");
        _ = AddCorePack("11.0.0", "net10.0");
        _ = AddCorePack("9.0.20", "net10.0");

        Assert.Equal(expected, SdkDiscovery.FindHighestVersionRefPath(root, "net10.0"));
    }

    [Fact]
    public void AFolderNotNamedAsAVersion_IsPassedOver()
    {
        var expected = AddCorePack("10.0.1", "net10.0");
        _ = AddCorePack("10.0.2-rc.1.25451.107", "net10.0");
        _ = AddCorePack("latest", "net10.0");

        Assert.Equal(expected, SdkDiscovery.FindHighestVersionRefPath(root, "net10.0"));
    }

    [Fact]
    public void OnlyFoldersNotNamedAsAVersion_FindNothing()
    {
        _ = AddCorePack("10.0.2-rc.1.25451.107", "net10.0");

        Assert.Null(SdkDiscovery.FindHighestVersionRefPath(root, "net10.0"));
    }

    [Fact]
    public void ATfmWithoutAMajor_TakesTheHighestPackHoldingItsFolder_OfAnyMajor()
    {
        _ = AddCorePack("9.0.20", "netstandard2.0");
        var expected = AddCorePack("10.0.1", "netstandard2.0");
        _ = AddCorePack("11.0.0", "net11.0");

        Assert.Equal(expected, SdkDiscovery.FindHighestVersionRefPath(root, "netstandard2.0"));
    }
}