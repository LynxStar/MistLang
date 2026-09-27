using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

// The cache decides which SDK reference directory answers a requested target framework:
// a remembered one, a persisted one, or a fresh discovery. Every collaborator is a
// hand-built fake below, so these run without a filesystem, a dotnet process or ~/.mist.
[Trait("Tier", "1")]
public sealed class SdkReferencePathCacheTests
{
    private const string Net10 = "net10.0";
    private const string Net8 = "net8.0";

    [Theory]
    [InlineData(Net10, Net8)]
    [InlineData(Net8, Net10)]
    public void SecondFramework_IsDiscoveredForItself_InEitherOrder(string first, string second)
    {
        var sdk = new FakeSdk();
        var firstPath = sdk.Install(first);
        var secondPath = sdk.Install(second);
        var cache = sdk.NewCache();

        Assert.Equal(firstPath, cache.Find(first, new DiagnosticBag()));
        Assert.Equal(secondPath, cache.Find(second, new DiagnosticBag()));
        Assert.Equal(firstPath, cache.Find(first, new DiagnosticBag()));
        Assert.Equal(secondPath, cache.Find(second, new DiagnosticBag()));

        Assert.Equal([first, second], sdk.Discoveries);
    }

    [Fact]
    public void MatchingEntry_IsReused_FromMemoryAndFromTheStore()
    {
        var sdk = new FakeSdk();
        var path = sdk.Install(Net10);

        Assert.Equal(path, sdk.NewCache().Find(Net10, new DiagnosticBag()));
        var reopened = sdk.NewCache();
        Assert.Equal(path, reopened.Find(Net10, new DiagnosticBag()));
        Assert.Equal(path, reopened.Find(Net10, new DiagnosticBag()));

        Assert.Equal([Net10], sdk.Discoveries);
    }

    [Fact]
    public void RememberedPath_AnswersOnlyItsOwnFramework()
    {
        var sdk = new FakeSdk { PersistenceDisabled = true };
        _ = sdk.Install(Net10);
        var net8Path = sdk.Install(Net8);
        var cache = sdk.NewCache();
        _ = cache.Find(Net10, new DiagnosticBag());

        Assert.Equal(net8Path, cache.Find(Net8, new DiagnosticBag()));

        Assert.Equal([Net10, Net8], sdk.Discoveries);
    }

    [Fact]
    public void PersistedPath_AnswersOnlyItsOwnFramework()
    {
        var sdk = new FakeSdk();
        var net10Path = sdk.Install(Net10);
        var net8Path = sdk.Install(Net8);
        sdk.Persisted = $"{Net10}\t{net10Path}";

        Assert.Equal(net8Path, sdk.NewCache().Find(Net8, new DiagnosticBag()));

        Assert.Equal([Net8], sdk.Discoveries);
    }

    [Fact]
    public void FrameworkSharingAMajorVersion_IsNotAnswered()
    {
        var sdk = new FakeSdk();
        _ = sdk.Install(Net10);
        var windowsPath = sdk.Install("net10.0-windows");
        var cache = sdk.NewCache();
        _ = cache.Find(Net10, new DiagnosticBag());

        Assert.Equal(windowsPath, cache.Find("net10.0-windows", new DiagnosticBag()));
        Assert.Equal(windowsPath, sdk.NewCache().Find("net10.0-windows", new DiagnosticBag()));

        Assert.Equal([Net10, "net10.0-windows"], sdk.Discoveries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableFramework_StillFails_WhileAnotherIsCached(bool reopened)
    {
        var sdk = new FakeSdk();
        var net10Path = sdk.Install(Net10);
        var cache = sdk.NewCache();
        _ = cache.Find(Net10, new DiagnosticBag());
        var persistedBefore = sdk.Persisted;
        var diagnostics = new DiagnosticBag();

        var path = (reopened ? sdk.NewCache() : cache).Find(Net8, diagnostics);

        Assert.Null(path);
        var failure = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(CommonDiagnostics.SdkNoFramework.Id, failure.Id);
        Assert.Contains(Net8, failure.Message, StringComparison.Ordinal);
        Assert.Equal([Net10, Net8], sdk.Discoveries);
        Assert.Equal(persistedBefore, sdk.Persisted);
        Assert.Equal(net10Path, cache.Find(Net10, new DiagnosticBag()));
    }

    [Fact]
    public void FailedDiscovery_IsNotRemembered()
    {
        var sdk = new FakeSdk();
        var cache = sdk.NewCache();
        Assert.Null(cache.Find(Net8, new DiagnosticBag()));
        var path = sdk.Install(Net8);

        Assert.Equal(path, cache.Find(Net8, new DiagnosticBag()));

        Assert.Equal([Net8, Net8], sdk.Discoveries);
    }

    [Fact]
    public void StaleRememberedPath_IsRediscovered_OnlyForItsFramework()
    {
        var sdk = new FakeSdk();
        var oldNet10 = sdk.Install(Net10);
        var net8Path = sdk.Install(Net8);
        var cache = sdk.NewCache();
        _ = cache.Find(Net10, new DiagnosticBag());
        _ = cache.Find(Net8, new DiagnosticBag());
        sdk.Uninstall(Net10);
        var newNet10 = sdk.Install(Net10, "pack-2");

        Assert.Equal(newNet10, cache.Find(Net10, new DiagnosticBag()));
        Assert.Equal(net8Path, cache.Find(Net8, new DiagnosticBag()));

        Assert.NotEqual(oldNet10, newNet10);
        Assert.Equal([Net10, Net8, Net10], sdk.Discoveries);
        Assert.Equal($"{Net10}\t{newNet10}\n{Net8}\t{net8Path}", sdk.Persisted);
    }

    [Fact]
    public void StalePersistedPath_IsRediscovered()
    {
        var sdk = new FakeSdk { Persisted = $"{Net10}\t/dotnet/removed/ref/{Net10}" };
        var path = sdk.Install(Net10);

        Assert.Equal(path, sdk.NewCache().Find(Net10, new DiagnosticBag()));

        Assert.Equal([Net10], sdk.Discoveries);
        Assert.Equal($"{Net10}\t{path}", sdk.Persisted);
    }

    [Theory]
    [InlineData(Net10)]
    [InlineData(Net8)]
    public void UnqualifiedPath_IsAMissForEveryFramework_AndIsRewrittenQualified(string requested)
    {
        var sdk = new FakeSdk();
        var net10Path = sdk.Install(Net10);
        var requestedPath = sdk.Install(requested);
        sdk.Persisted = net10Path;

        Assert.Equal(requestedPath, sdk.NewCache().Find(requested, new DiagnosticBag()));

        Assert.Equal([requested], sdk.Discoveries);
        Assert.Equal($"{requested}\t{requestedPath}", sdk.Persisted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r\n")]
    [InlineData("net10.0")]
    [InlineData("net10.0\t")]
    [InlineData("\t{path}")]
    [InlineData("net10.0 {path}")]
    [InlineData("net10.0{path}")]
    public void MalformedStore_IsAMiss(string persisted)
    {
        var sdk = new FakeSdk();
        var path = sdk.Install(Net10);
        sdk.Persisted = persisted.Replace("{path}", path, StringComparison.Ordinal);

        Assert.Equal(path, sdk.NewCache().Find(Net10, new DiagnosticBag()));

        Assert.Equal([Net10], sdk.Discoveries);
    }

    [Fact]
    public void UnreadableStore_StillDiscovers_AndRemembers()
    {
        var sdk = new FakeSdk { ReadFailure = new IOException("locked") };
        var path = sdk.Install(Net10);
        var cache = sdk.NewCache();

        Assert.Equal(path, cache.Find(Net10, new DiagnosticBag()));
        Assert.Equal(path, cache.Find(Net10, new DiagnosticBag()));

        Assert.Equal([Net10], sdk.Discoveries);
    }

    [Fact]
    public void UnwritableStore_StillDiscovers_AndRemembers()
    {
        var sdk = new FakeSdk { WriteFailure = new UnauthorizedAccessException("read-only") };
        var path = sdk.Install(Net10);
        var cache = sdk.NewCache();
        var diagnostics = new DiagnosticBag();

        Assert.Equal(path, cache.Find(Net10, diagnostics));
        Assert.Equal(path, cache.Find(Net10, new DiagnosticBag()));

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal([Net10], sdk.Discoveries);
        Assert.Null(sdk.Persisted);
    }

    [Fact]
    public void Regeneration_RediscoversItsFramework_AndClaimsNoOther()
    {
        var sdk = new FakeSdk();
        _ = sdk.Install(Net10);
        var net8Path = sdk.Install(Net8);
        var cache = sdk.NewCache();
        _ = cache.Find(Net10, new DiagnosticBag());
        var upgraded = sdk.Install(Net10, "pack-2");

        Assert.Equal(upgraded, cache.Regenerate(Net10, new DiagnosticBag()));
        Assert.Equal(upgraded, cache.Find(Net10, new DiagnosticBag()));
        Assert.Equal(net8Path, cache.Find(Net8, new DiagnosticBag()));

        Assert.Equal([Net10, Net10, Net8], sdk.Discoveries);
        Assert.Equal($"{Net10}\t{upgraded}\n{Net8}\t{net8Path}", sdk.Persisted);
    }

    [Fact]
    public void FrameworkThatCannotBeWrittenAsALine_IsRememberedButNotPersisted()
    {
        var sdk = new FakeSdk();
        var net8Path = sdk.Install(Net8);
        var injected = $"x\n{Net8}";
        var net10Path = sdk.Install(injected, path: $"/dotnet/packs/Microsoft.NETCore.App.Ref/pack-1/ref/{Net10}");
        var cache = sdk.NewCache();

        Assert.Equal(net10Path, cache.Find(injected, new DiagnosticBag()));
        Assert.Equal(net10Path, cache.Find(injected, new DiagnosticBag()));
        Assert.Equal(net8Path, sdk.NewCache().Find(Net8, new DiagnosticBag()));

        Assert.Equal([injected, Net8], sdk.Discoveries);
    }

    [Fact]
    public async Task SimultaneousDiscoveries_EachPublishTheirOwnEntry()
    {
        var sdk = new FakeSdk();
        var net10Path = sdk.Install(Net10);
        var net8Path = sdk.Install(Net8);
        using var bothDiscovering = new Barrier(2);
        sdk.DuringDiscovery = () => bothDiscovering.SignalAndWait(TimeSpan.FromSeconds(10));
        var cache = sdk.NewCache();

        var answers = await Task.WhenAll(
            Task.Run(() => cache.Find(Net10, new DiagnosticBag())),
            Task.Run(() => cache.Find(Net8, new DiagnosticBag())));
        sdk.DuringDiscovery = null;

        Assert.Equal(net10Path, answers[0]);
        Assert.Equal(net8Path, answers[1]);
        Assert.Equal($"{Net10}\t{net10Path}\n{Net8}\t{net8Path}", sdk.Persisted);
        var reopened = sdk.NewCache();
        Assert.Equal(net10Path, reopened.Find(Net10, new DiagnosticBag()));
        Assert.Equal(net8Path, reopened.Find(Net8, new DiagnosticBag()));
        Assert.Equal(2, sdk.Discoveries.Count);
    }

    [Fact]
    public void ConcurrentRequests_NeverCrossFrameworks()
    {
        string[] frameworks = [Net10, "net9.0", Net8];
        var sdk = new FakeSdk();
        var paths = frameworks.ToDictionary(framework => framework, framework => sdk.Install(framework));
        var cache = sdk.NewCache();
        var answers = new string?[300];

        _ = Parallel.For(0, answers.Length, i => answers[i] = cache.Find(frameworks[i % 3], new DiagnosticBag()));

        for(var i = 0; i < answers.Length; i++)
        {
            Assert.Equal(paths[frameworks[i % 3]], answers[i]);
        }

        var reopened = sdk.NewCache();
        var discovered = sdk.Discoveries.Count;
        Assert.All(frameworks, framework => Assert.Equal(paths[framework], reopened.Find(framework, new DiagnosticBag())));
        Assert.Equal(discovered, sdk.Discoveries.Count);
    }

    private sealed class FakeSdk
    {
        private readonly object gate = new();
        private readonly Dictionary<string, string> installed = new(StringComparer.Ordinal);
        private readonly HashSet<string> directories = new(StringComparer.Ordinal);
        private readonly List<string> discoveries = [];

        public string? Persisted { get; set; }

        public bool PersistenceDisabled { get; init; }

        public Exception? ReadFailure { get; init; }

        public Exception? WriteFailure { get; init; }

        public Action? DuringDiscovery { get; set; }

        public IReadOnlyList<string> Discoveries
        {
            get
            {
                lock(gate)
                {
                    return [.. discoveries];
                }
            }
        }

        public string Install(string targetFramework, string pack = "pack-1", string? path = null)
        {
            path ??= $"/dotnet/packs/Microsoft.NETCore.App.Ref/{pack}/ref/{targetFramework}";
            lock(gate)
            {
                installed[targetFramework] = path;
                _ = directories.Add(path);
            }

            return path;
        }

        public void Uninstall(string targetFramework)
        {
            lock(gate)
            {
                _ = directories.Remove(installed[targetFramework]);
                _ = installed.Remove(targetFramework);
            }
        }

        public SdkReferencePathCache NewCache()
        {
            return new SdkReferencePathCache(Discover, Exists, Read, Write);
        }

        private string? Discover(string targetFramework, DiagnosticBag diagnostics)
        {
            lock(gate)
            {
                discoveries.Add(targetFramework);
            }

            DuringDiscovery?.Invoke();
            lock(gate)
            {
                if(installed.TryGetValue(targetFramework, out var path))
                {
                    return path;
                }
            }

            diagnostics.Add(new Diagnostic(
                CommonDiagnostics.SdkNoFramework.Id,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"The target framework '{targetFramework}' reference assemblies were not found in the SDK at '/dotnet'.",
                Location: default,
                CompilationUnitPath: "<sdk-discovery>",
                AnalyzerName: null));
            return null;
        }

        private bool Exists(string path)
        {
            lock(gate)
            {
                return directories.Contains(path);
            }
        }

        private string? Read()
        {
            if(ReadFailure is not null)
            {
                throw ReadFailure;
            }

            return PersistenceDisabled ? null : Persisted;
        }

        private void Write(string contents)
        {
            if(WriteFailure is not null)
            {
                throw WriteFailure;
            }

            if(!PersistenceDisabled)
            {
                Persisted = contents;
            }
        }
    }
}