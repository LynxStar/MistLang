using System.Text.Json;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class PackageAssetsEmptyFolderMapTests
{
    [Fact]
    public void EmptyPackageFolders_UsesTheRecordedRestorePackagesPath()
    {
        using var document = JsonDocument.Parse("""
            {
              "packageFolders": {},
              "project": { "restore": { "packagesPath": "/restore/nuget/" } }
            }
            """);

        var folders = PackageAssets.PackageFolders(document.RootElement, "/default/nuget/");

        Assert.Equal<string>(["/restore/nuget/"], folders);
    }
}