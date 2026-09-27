using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

// The persistence seam against a real filesystem: one private temp directory per test,
// never ~/.mist. SDK discovery and directory existence stay fakes, so no dotnet process runs.
[Trait("Tier", "2")]
public sealed class SdkReferencePathCacheFileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"mist_sdk_cache_{Guid.NewGuid():N}");

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

    [Fact]
    public void MissingFile_ReadsAsNothing()
    {
        Assert.Null(new SdkReferencePathCacheFile(Path.Combine(root, "sdk-ref-path.cache")).Read());
    }

    [Fact]
    public void Write_CreatesTheDirectory_AndReplacesContentsWhole()
    {
        var path = Path.Combine(root, ".mist", "sdk-ref-path.cache");
        var file = new SdkReferencePathCacheFile(path);

        file.Write("net10.0\t/a/much/longer/first/path\nnet8.0\t/b");
        file.Write("net8.0\t/c");

        Assert.Equal("net8.0\t/c", file.Read());
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void EntriesForSeveralFrameworks_SurviveTheFile()
    {
        var file = new SdkReferencePathCacheFile(Path.Combine(root, "sdk-ref-path.cache"));
        var discoveries = new List<string>();
        var paths = new Dictionary<string, string>
        {
            ["net10.0"] = @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0",
            ["net8.0"] = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.0/ref/net8.0",
        };
        SdkReferencePathCache newCache()
        {
            return new SdkReferencePathCache(
                (framework, _) =>
                {
                    discoveries.Add(framework);
                    return paths[framework];
                },
                paths.ContainsValue,
                file.Read,
                file.Write);
        }

        var first = newCache();
        _ = first.Find("net10.0", new DiagnosticBag());
        _ = first.Find("net8.0", new DiagnosticBag());
        var reopened = newCache();

        Assert.Equal(paths["net10.0"], reopened.Find("net10.0", new DiagnosticBag()));
        Assert.Equal(paths["net8.0"], reopened.Find("net8.0", new DiagnosticBag()));
        Assert.Equal(["net10.0", "net8.0"], discoveries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnusableCacheLocation_DoesNotPreventDiscovery(bool cachePathIsADirectory)
    {
        _ = Directory.CreateDirectory(root);
        string cachePath;
        if(cachePathIsADirectory)
        {
            cachePath = Directory.CreateDirectory(Path.Combine(root, "sdk-ref-path.cache")).FullName;
        }
        else
        {
            var blocker = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blocker, "");
            cachePath = Path.Combine(blocker, "sdk-ref-path.cache");
        }

        var file = new SdkReferencePathCacheFile(cachePath);
        const string discovered = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0";
        var cache = new SdkReferencePathCache((_, _) => discovered, path => path == discovered, file.Read, file.Write);
        var diagnostics = new DiagnosticBag();

        Assert.Equal(discovered, cache.Find("net10.0", diagnostics));
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Null(file.Read());
        _ = Assert.Single(Directory.GetFileSystemEntries(root));
    }
}