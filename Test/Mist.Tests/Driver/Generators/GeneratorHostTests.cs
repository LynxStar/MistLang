using System.Text;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Driver.Generators;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "1")]
public sealed class GeneratorHostTests
{
    [Fact]
    public void ColdCompileAndRunThenWarmHitsPreserveTheRequestProtocol()
    {
        var fixture = new Fixture();
        var first = fixture.Host.Prepare(fixture.Request, new DiagnosticBag());
        var second = fixture.Host.Prepare(fixture.Request, new DiagnosticBag());
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, fixture.Compiles);
        var invocation = Assert.Single(fixture.Invocations);
        Assert.True(invocation.CleanEnvironment);
        Assert.Equal(invocation.WorkingDirectory, invocation.Environment["MIST_GENERATOR_STAGING"]);
        Assert.Single(invocation.Environment);
        Assert.Equal(fixture.Request.Reference.TimeLimit, invocation.Timeout);
        var request = fixture.Files.Text(Path.Combine(invocation.WorkingDirectory, "request.txt")).Split('\n');
        Assert.Equal("one", fixture.Files.Text(request[0]));
        Assert.Equal("two", fixture.Files.Text(request[1]));
        Assert.Equal("", request[2]);
        Assert.Equal("arg", request[3]);
        Assert.Contains("inputs", request[0]);
        Assert.Contains("/consumer/grammar/a.txt", request[0].Replace('\\', '/'));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("exit")]
    [InlineData("list")]
    [InlineData("protocol")]
    public void FailedRunsPublishNothingAndRetainEarlierRuns(string mode)
    {
        var fixture = new Fixture();
        var previous = fixture.Host.Prepare(fixture.Request, new DiagnosticBag());
        fixture.Mode = mode;
        var changed = fixture.Request with { Reference = fixture.Request.Reference with { Arguments = ["different"] } };
        var diagnostics = new DiagnosticBag();
        Assert.Null(fixture.Host.Prepare(changed, diagnostics));
        Assert.True(diagnostics.HasErrors);
        Assert.Single(fixture.Cache.Entries, entry => entry.Key.StartsWith("runs", StringComparison.Ordinal));
        Assert.Contains(previous!, fixture.Cache.Entries.Values);
        Assert.Same(mode is "list" or "protocol" ? DriverDiagnostics.GeneratorOutputInvalid : DriverDiagnostics.GeneratorRunFailed,
            Assert.Single(diagnostics.Admitted).Descriptor);
    }

    [Theory]
    [InlineData("warning", false)]
    [InlineData("error", true)]
    public void ProviderCodesAndUnparsedMessagesSurviveIncludingWarmWarnings(string severity, bool error)
    {
        var fixture = new Fixture { Mode = severity };
        var diagnostics = new DiagnosticBag();
        var result = fixture.Host.Prepare(fixture.Request, diagnostics);
        Assert.Equal(error, result is null);
        var diagnostic = Assert.Single(diagnostics.Admitted);
        Assert.Same(error ? DriverDiagnostics.GeneratorReportedError : DriverDiagnostics.GeneratorReportedWarning, diagnostic.Descriptor);
        Assert.Equal(new ProviderCode("G", "G0001", error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning), diagnostic.Native);
        Assert.Equal("text: remains {unparsed}", diagnostic.Message);
        Assert.Equal(2, diagnostic.Location.Lines!.Value.Start.Line);
        Assert.Equal(3, diagnostic.Location.Lines.Value.Start.Column);
        Assert.Equal("/consumer/grammar/a.txt", diagnostic.Location.Path);
        Assert.Equal(error, diagnostics.HasErrors);
        if(!error)
        {
            var warm = new DiagnosticBag();
            Assert.NotNull(fixture.Host.Prepare(fixture.Request, warm));
            Assert.Equal(diagnostic.Native, Assert.Single(warm.Admitted).Native);
            Assert.Single(fixture.Invocations);
        }
    }

    [Fact]
    public void CompileFailureStopsBeforeRun()
    {
        var fixture = new Fixture { BuildFails = true };
        var diagnostics = new DiagnosticBag();
        Assert.Null(fixture.Host.Prepare(fixture.Request, diagnostics));
        Assert.Same(DriverDiagnostics.GeneratorBuildFailed, Assert.Single(diagnostics.Admitted).Descriptor);
        Assert.Empty(fixture.Invocations);
        Assert.Empty(fixture.Cache.Entries);
    }

    [Fact]
    public void MissingInputStopsBeforeCompile()
    {
        var fixture = new Fixture();
        _ = fixture.Files.Content.Remove("/consumer/grammar/a.txt");
        var diagnostics = new DiagnosticBag();
        Assert.Null(fixture.Host.Prepare(fixture.Request, diagnostics));
        Assert.Same(DriverDiagnostics.GeneratorInputNotFound, Assert.Single(diagnostics.Admitted).Descriptor);
        Assert.Equal(0, fixture.Compiles);
    }

    [Fact]
    public void RunMissReusesCompiledGenerator()
    {
        var fixture = new Fixture();
        Assert.NotNull(fixture.Host.Prepare(fixture.Request, new DiagnosticBag()));
        Assert.NotNull(fixture.Host.Prepare(fixture.Request with { ConsumerName = "Other" }, new DiagnosticBag()));
        Assert.Equal(1, fixture.Compiles);
        Assert.Equal(2, fixture.Invocations.Count);
    }

    [Fact]
    public void SeedFlagIsRecordedOnEveryRun()
    {
        var fixture = new Fixture();
        var seeded = fixture.Request with { Reference = fixture.Request.Reference with { Seed = true } };
        var entry = fixture.Host.Prepare(seeded, new DiagnosticBag());
        Assert.True(entry!.Provenance.SeedTool);
        Assert.Equal(seeded.ToolchainIdentity, entry.Provenance.ToolchainIdentity);
        Assert.Equal(seeded.CompileKey, entry.Provenance.GeneratorIdentity);
        Assert.Equal(seeded.ConsumerName, entry.Provenance.ConsumerName);
        Assert.Equal(seeded.ConsumerManifest, entry.Provenance.ConsumerManifestPath);
        Assert.Equal(seeded.Inputs, entry.Provenance.Inputs);
        Assert.Equal(seeded.Reference.Arguments, entry.Provenance.Arguments);
    }

    private sealed class Fixture
    {
        public MemoryFiles Files { get; } = new();
        public MemoryCache Cache { get; } = new();
        public List<DotnetInvocation> Invocations { get; } = [];
        public GeneratorHost Host { get; }
        public GeneratorRequest Request { get; }
        public int Compiles { get; private set; }
        public bool BuildFails { get; init; }
        public string Mode { get; set; } = "success";

        public Fixture()
        {
            Files.Put("/consumer/grammar/a.txt", "one");
            Files.Put("/consumer/grammar/b.txt", "two");
            var reference = new GeneratorReference("G", "/G/mist.toml", ["/consumer/grammar/a.txt", "/consumer/grammar/b.txt"], ["arg"], TimeSpan.FromSeconds(10));
            Request = new GeneratorRequest("tool", "compiled-key", "Consumer", "/consumer/mist.toml", reference,
                new ProjectConfiguration("G", "G", [], ProjectType: MistProjectType.SourceGenerator),
                [.. reference.Inputs.Select(path => new ContentHash(path, GeneratorProvenance.Hash(Files.Read(path))))]);
            Host = new GeneratorHost(Cache, Files, invocation =>
            {
                Invocations.Add(invocation);
                var folder = invocation.WorkingDirectory;
                Files.Put(Path.Combine(folder, "Generated.mist"), "namespace Demo; class Generated {}");
                Files.Put(Path.Combine(folder, "outputs.txt"), Mode == "list" ? "../escape.mist" : "Generated.mist");
                Files.Put(Path.Combine(folder, "diagnostics.txt"), Mode is "warning" or "error"
                    ? $"/consumer/grammar/a.txt(2,3): {Mode} G0001: text: remains {{unparsed}}"
                    : Mode == "protocol" ? "invalid" : "");
                return new DotnetOutcome(Mode == "exit" ? 1 : 0, "captured", "error detail", Mode == "timeout");
            }, (_, staging, _) =>
            {
                Compiles++;
                return BuildFails ? null : Path.Combine(staging, "G.dll");
            });
        }
    }

    private sealed class MemoryCache : IGeneratorCache
    {
        public Dictionary<string, CachedGeneration> Entries { get; } = new();

        public CachedGeneration? Use(string kind, string key, TimeSpan timeout, Func<string, ProvenanceRecord?> produce)
        {
            var identity = kind + key;
            if(Entries.TryGetValue(identity, out var cached))
            {
                return cached;
            }

            var staging = "/cache/" + kind + "/" + key;
            if(produce(staging) is not { } record)
            {
                return null;
            }

            var entry = new CachedGeneration(staging, record);
            Entries.Add(identity, entry);
            return entry;
        }
    }

    private sealed class MemoryFiles : IGeneratorFiles
    {
        public Dictionary<string, byte[]> Content { get; } = new(StringComparer.Ordinal);

        public bool Exists(string path)
        {
            return Content.ContainsKey(path);
        }

        public byte[] Read(string path)
        {
            return Content[path];
        }

        public void Write(string path, byte[] content)
        {
            Content[path] = content;
        }

        public IEnumerable<string> Files(string folder)
        {
            return Content.Keys.Where(path => path.StartsWith(folder + "/", StringComparison.Ordinal));
        }

        public string Text(string path)
        {
            return Encoding.UTF8.GetString(Read(path));
        }

        public void Put(string path, string content)
        {
            Write(path, Encoding.UTF8.GetBytes(content));
        }
    }
}