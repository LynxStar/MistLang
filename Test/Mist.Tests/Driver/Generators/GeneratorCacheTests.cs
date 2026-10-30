using Mist.Driver.Generators;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "3")]
public sealed class GeneratorCacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mist-generator-cache-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PublicationVerifiesContentAndWarmReuseDoesNotWrite()
    {
        var cache = new GeneratorCache(root, "tool");
        var entry = cache.Use("runs", "key", TimeSpan.FromSeconds(1), Produce)!;
        var file = Path.Combine(entry.Folder, "Generated.mist");
        var mtime = File.GetLastWriteTimeUtc(file);
        var warm = cache.Use("runs", "key", TimeSpan.FromSeconds(1), _ => throw new InvalidOperationException());
        Assert.Equal(entry.Folder, warm!.Folder);
        Assert.Equal(GeneratorProvenance.Serialize(entry.Provenance), GeneratorProvenance.Serialize(warm.Provenance));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(file));
        Assert.NotNull(GeneratorCache.Read(entry.Folder, "key"));
    }

    [Theory]
    [InlineData("tamper")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("record")]
    public void DamagedEntriesForceRegeneration(string damage)
    {
        var cache = new GeneratorCache(root, "tool");
        var first = cache.Use("runs", "key", TimeSpan.FromSeconds(1), Produce)!;
        var file = Path.Combine(first.Folder, "Generated.mist");
        switch(damage)
        {
            case "tamper":
                var mtime = File.GetLastWriteTimeUtc(file);
                File.WriteAllText(file, "edit");
                File.SetLastWriteTimeUtc(file, mtime);
                break;
            case "missing": File.Delete(file); break;
            case "extra": File.WriteAllText(Path.Combine(first.Folder, "extra.txt"), "extra"); break;
            case "record": File.Delete(Path.Combine(first.Folder, GeneratorProvenance.FileName)); break;
        }

        var generated = 0;
        Assert.NotNull(cache.Use("runs", "key", TimeSpan.FromSeconds(1), staging =>
        {
            generated++;
            return Produce(staging);
        }));
        Assert.Equal(1, generated);
        Assert.Equal("mist", File.ReadAllText(file));
    }

    [Fact]
    public void FailedRegenerationLeavesPublishedRunAndDeletesStaging()
    {
        var cache = new GeneratorCache(root, "tool");
        var entry = cache.Use("runs", "key", TimeSpan.FromSeconds(1), Produce)!;
        File.WriteAllText(Path.Combine(entry.Folder, "Generated.mist"), "edit");
        Assert.Null(cache.Use("runs", "key", TimeSpan.FromSeconds(1), _ => null));
        Assert.Equal("edit", File.ReadAllText(Path.Combine(entry.Folder, "Generated.mist")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(entry.Folder)!, "*.staging-*"));
    }

    [Fact]
    public void CompetingPublicationReusesTheWinner()
    {
        var cache = new GeneratorCache(root, "tool");
        var winner = cache.Use("runs", "key", TimeSpan.FromSeconds(1), Produce)!;
        var losing = Path.Combine(root, "losing");
        _ = Directory.CreateDirectory(losing);
        File.WriteAllText(Path.Combine(losing, "Generated.mist"), "loser");
        Assert.Equal(winner.Folder, GeneratorCache.Publish(losing, winner.Folder, "key")!.Folder);
        Assert.Equal("mist", File.ReadAllText(Path.Combine(winner.Folder, "Generated.mist")));
    }

    [Fact]
    public async Task WaiterRechecksAndReusesAfterTheLockOpens()
    {
        var guardTimeout = TimeSpan.FromSeconds(30);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var contended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new GeneratorCache(root, "tool", waiting: () => contended.TrySetResult());
        var publisher = Task.Factory.StartNew(() => cache.Use("runs", "key", guardTimeout, staging =>
        {
            entered.Set();
            Assert.True(release.Wait(guardTimeout));
            return Produce(staging);
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var waiter = Task.Factory.StartNew(() =>
        {
            Assert.True(entered.Wait(guardTimeout));
            return cache.Use("runs", "key", guardTimeout, _ => throw new InvalidOperationException("waiter generated"));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await contended.Task.WaitAsync(guardTimeout);
        }
        finally
        {
            release.Set();
            _ = await Task.WhenAll(publisher, waiter);
        }

        var published = await publisher;
        var reused = await waiter;
        Assert.Equal(published!.Folder, reused!.Folder);
        Assert.Equal(GeneratorProvenance.Serialize(published.Provenance), GeneratorProvenance.Serialize(reused.Provenance));
    }

    private static ProvenanceRecord Produce(string staging)
    {
        File.WriteAllText(Path.Combine(staging, "Generated.mist"), "mist");
        return new ProvenanceRecord("tool", "compiled", "Consumer", "/consumer/mist.toml", [], [], "key", [], false);
    }

    public void Dispose()
    {
        if(Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}