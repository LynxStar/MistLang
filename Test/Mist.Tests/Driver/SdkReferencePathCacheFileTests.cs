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
    public void Update_CreatesTheDirectory_AndReplacesContentsWhole()
    {
        var path = Path.Combine(root, ".mist", "sdk-ref-path.cache");
        var file = new SdkReferencePathCacheFile(path);

        file.Update(_ => "net10.0\t/a/much/longer/first/path\nnet8.0\t/b");
        file.Update(_ => "net8.0\t/c");

        Assert.Equal("net8.0\t/c", file.Read());
        Assert.Equal([path, $"{path}.lock"], Directory.GetFiles(Path.GetDirectoryName(path)!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Update_RevisesTheContentsAnotherWriterCommitted()
    {
        var path = Path.Combine(root, "sdk-ref-path.cache");
        new SdkReferencePathCacheFile(path).Update(_ => "net10.0\t/a");
        string? seen = null;

        new SdkReferencePathCacheFile(path).Update(current =>
        {
            seen = current;
            return $"{current}\nnet8.0\t/b";
        });

        Assert.Equal("net10.0\t/a", seen);
        Assert.Equal("net10.0\t/a\nnet8.0\t/b", File.ReadAllText(path));
    }

    [Fact]
    public async Task ContendedUpdate_WaitsForTheHolder_AndKeepsBothRevisions()
    {
        var path = Path.Combine(root, "sdk-ref-path.cache");
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => new SdkReferencePathCacheFile(path).Update(_ =>
        {
            holding.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return "net10.0\t/a";
        }));
        Assert.True(holding.Wait(TimeSpan.FromSeconds(10)));
        string? seen = null;
        var contender = Task.Run(() => new SdkReferencePathCacheFile(path, TimeSpan.FromSeconds(10)).Update(current =>
        {
            seen = current;
            return $"{current}\nnet8.0\t/b";
        }));

        release.Set();
        await Task.WhenAll(holder, contender);

        Assert.Equal("net10.0\t/a", seen);
        Assert.Equal("net10.0\t/a\nnet8.0\t/b", new SdkReferencePathCacheFile(path).Read());
    }

    [Fact]
    public void HeldLock_BoundsTheWait_AndDiscoveryStillAnswers()
    {
        var path = Path.Combine(root, "sdk-ref-path.cache");
        var file = new SdkReferencePathCacheFile(path, TimeSpan.FromMilliseconds(50));
        file.Update(_ => "net8.0\t/b");
        const string discovered = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0";
        var cache = new SdkReferencePathCache((_, _) => discovered, candidate => candidate == discovered, file.Read, file.Update);
        var diagnostics = new DiagnosticBag();

        using(new FileStream($"{path}.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            _ = Assert.ThrowsAny<IOException>(() => file.Update(_ => throw new InvalidOperationException("entered")));
            Assert.Equal(discovered, cache.Find("net10.0", diagnostics));
        }

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal("net8.0\t/b", file.Read());
        Assert.Equal(discovered, cache.Find("net10.0", new DiagnosticBag()));
    }

    [Fact]
    public void FailedRevision_LeavesTheFileUnchanged_AndReleasesTheLock()
    {
        var path = Path.Combine(root, "sdk-ref-path.cache");
        var file = new SdkReferencePathCacheFile(path, TimeSpan.Zero);
        file.Update(_ => "net8.0\t/b");

        _ = Assert.Throws<InvalidOperationException>(() => file.Update(_ => throw new InvalidOperationException("revision")));
        file.Update(current => $"{current}\nnet10.0\t/a");

        Assert.Equal("net8.0\t/b\nnet10.0\t/a", file.Read());
        Assert.DoesNotContain(Directory.GetFiles(root), entry => entry.EndsWith(".tmp", StringComparison.Ordinal));
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
                file.Update);
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
        var cache = new SdkReferencePathCache((_, _) => discovered, path => path == discovered, file.Read, file.Update);
        var diagnostics = new DiagnosticBag();

        Assert.Equal(discovered, cache.Find("net10.0", diagnostics));
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Null(file.Read());
        Assert.DoesNotContain(Directory.GetFileSystemEntries(root), entry => entry.EndsWith(".tmp", StringComparison.Ordinal));
    }
}