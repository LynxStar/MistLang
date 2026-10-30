using System.Text;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Driver.Generators;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "1")]
public sealed class GeneratorSeedsTests
{
    [Fact]
    public void ValidDeclaredSourcesSelectTheCapturedSetAndReportOnce()
    {
        var files = new MemoryFiles();
        var diagnostics = new DiagnosticBag();
        var prepared = GeneratorSeeds.Prepare(Consumer(), files, diagnostics);
        Assert.NotNull(prepared);
        Assert.Equal("/capture", prepared.Folder);
        Assert.Equal("Gen.mist", Assert.Single(prepared.Provenance.Outputs).Path);
        var diagnostic = Assert.Single(diagnostics.Admitted);
        Assert.Same(DriverDiagnostics.GeneratorSeedUsed, diagnostic.Descriptor);
        Assert.Equal(new ProjectLocation("/consumer/mist.toml", null), diagnostic.Location);
        Assert.Equal(new PathValue("/capture"), Assert.Single(diagnostic.Arguments));
    }

    [Theory]
    [InlineData("tamper")]
    [InlineData("missing-record")]
    [InlineData("bad-record")]
    [InlineData("missing-source")]
    [InlineData("extra-source")]
    [InlineData("escape")]
    public void UnattributableCapturedSetsAreRefused(string damage)
    {
        var files = new MemoryFiles();
        switch(damage)
        {
            case "tamper": files.Put("/capture/Gen.mist", "edited"); break;
            case "missing-record": _ = files.Content.Remove("/record.json"); break;
            case "bad-record": files.Put("/record.json", "{}"); break;
            case "missing-source": _ = files.Content.Remove("/capture/Gen.mist"); break;
            case "extra-source": files.Put("/capture/Extra.mist", "extra"); break;
            case "escape": files.Put("/record.json", GeneratorProvenance.Serialize(Record() with { Outputs = [new ContentHash("../Gen.mist", "hash")] })); break;
        }

        var diagnostics = new DiagnosticBag();
        Assert.Null(GeneratorSeeds.Prepare(Consumer(), files, diagnostics));
        Assert.Same(DriverDiagnostics.GeneratorReferenceInvalid, Assert.Single(diagnostics.Admitted).Descriptor);
    }

    [Fact]
    public void NoDeclarationReadsNoFilesAndReportsNoSeed()
    {
        var consumer = Consumer();
        consumer = consumer with { Configuration = consumer.Configuration with { SeedSources = null } };
        var diagnostics = new DiagnosticBag();
        Assert.Null(GeneratorSeeds.Prepare(consumer, new MemoryFiles(), diagnostics));
        Assert.Empty(diagnostics.Admitted);
    }

    [Fact]
    public void ProvenanceRoundTripsEveryFieldAndRejectsUnknownVersions()
    {
        var record = Record() with
        {
            Inputs = [new ContentHash("grammar", "input-hash")],
            Arguments = ["one", "two"],
            SeedTool = true,
            Files = [new ContentHash("request.txt", "request-hash")],
            ArtifactPath = "bin/G.dll"
        };
        var parsed = GeneratorProvenance.Deserialize(GeneratorProvenance.Serialize(record))!;
        Assert.Equal(record.ToolchainIdentity, parsed.ToolchainIdentity);
        Assert.Equal(record.GeneratorIdentity, parsed.GeneratorIdentity);
        Assert.Equal(record.ConsumerName, parsed.ConsumerName);
        Assert.Equal(record.ConsumerManifestPath, parsed.ConsumerManifestPath);
        Assert.Equal(record.RunKey, parsed.RunKey);
        Assert.Equal(record.SeedTool, parsed.SeedTool);
        Assert.Equal(record.ArtifactPath, parsed.ArtifactPath);
        Assert.Equal<ContentHash>(record.Inputs, parsed.Inputs);
        Assert.Equal<string>(record.Arguments, parsed.Arguments);
        Assert.Equal<ContentHash>(record.Outputs, parsed.Outputs);
        Assert.Equal<ContentHash>(record.Files, parsed.Files);
        Assert.Null(GeneratorProvenance.Deserialize(GeneratorProvenance.Serialize(record with { Version = 2 })));
    }

    [Fact]
    public void CapturedSourcesCannotCollideWithAGeneratorNamedSeedSources()
    {
        var consumer = Consumer();
        var captured = GeneratorPreparation.Location(consumer, "SeedSources", seedSources: true);
        var generated = GeneratorPreparation.Location(consumer, "SeedSources");
        Assert.NotEqual(captured.Name, generated.Name);
        Assert.NotEqual(captured.Folder, generated.Folder);
    }

    private static GeneratorNode Consumer()
    {
        return new GeneratorNode("/consumer/mist.toml", new ProjectConfiguration("Consumer", "Consumer", [], SeedSources: new SeedSources("/capture", "/record.json")));
    }

    private static ProvenanceRecord Record()
    {
        return new ProvenanceRecord("tool", "generator", "Consumer", "/consumer/mist.toml", [], [], "run",
            [new ContentHash("Gen.mist", GeneratorProvenance.Hash(Encoding.UTF8.GetBytes("source")))], false);
    }

    private sealed class MemoryFiles : IGeneratorFiles
    {
        public Dictionary<string, byte[]> Content { get; } = new()
        {
            ["/capture/Gen.mist"] = Encoding.UTF8.GetBytes("source"),
            ["/record.json"] = Encoding.UTF8.GetBytes(GeneratorProvenance.Serialize(Record()))
        };

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

        public void Put(string path, string text)
        {
            Write(path, Encoding.UTF8.GetBytes(text));
        }
    }
}