using System.Collections.Immutable;
using System.Text;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Driver.Generators;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "2")]
public sealed class GeneratorSeedDiscoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingAnAcceptedSeedDeclarationKeepsCapturedSourcesOutsideDiscovery(bool referenced)
    {
        var folder = Path.GetFullPath("consumer");
        var manifest = Path.Combine(folder, ProjectFileParser.FileName);
        var capture = Path.Combine(folder, "capture");
        var source = Path.Combine(capture, "nested", "Gen.mist");
        var program = Path.Combine(folder, "Program.mist");
        var provenance = Path.Combine(folder, "record.json");
        var record = new ProvenanceRecord("tool", "generator", "Consumer", manifest, [], [], "run",
            [new ContentHash(Path.Combine("nested", "Gen.mist"), GeneratorProvenance.Hash(Encoding.UTF8.GetBytes("source")))], false);
        var files = new MemoryFiles(new Dictionary<string, byte[]>
        {
            [program] = [],
            [source] = Encoding.UTF8.GetBytes("source"),
            [Path.Combine(capture, SourcePathSelection.GeneratedMarker)] = [],
            [provenance] = Encoding.UTF8.GetBytes(GeneratorProvenance.Serialize(record))
        });
        var consumer = new GeneratorNode(manifest,
            new ProjectConfiguration("Consumer", "Consumer", [], SeedSources: new SeedSources(capture, provenance)));
        var diagnostics = new DiagnosticBag();
        var accepted = GeneratorSeeds.Prepare(consumer, files, diagnostics);
        Assert.NotNull(accepted);
        Assert.Same(DriverDiagnostics.GeneratorSeedUsed, Assert.Single(diagnostics.Admitted).Descriptor);
        Assert.Equal(source, Path.Combine(accepted.Folder, Assert.Single(accepted.Provenance.Outputs).Path));
        Assert.Equal<string>([program], discover(consumer.Configuration));

        consumer = consumer with { Configuration = consumer.Configuration with { SeedSources = null } };
        diagnostics = new DiagnosticBag();
        Assert.Null(GeneratorSeeds.Prepare(consumer, files, diagnostics));
        Assert.Empty(diagnostics.Admitted);
        Assert.Equal<string>([program], discover(consumer.Configuration));

        ImmutableArray<string> discover(ProjectConfiguration configuration)
        {
            var candidates = files.Content.Keys.Where(path => path.EndsWith(".mist", StringComparison.Ordinal)
                || Path.GetFileName(path) == SourcePathSelection.GeneratedMarker).ToImmutableArray();
            if(!referenced)
            {
                return SourcePathSelection.Select(candidates, folder, Path.Combine(folder, "out"), Path.Combine(folder, "bin"));
            }

            var resolution = ProjectReferenceResolver.Resolve(Path.GetFullPath("root"),
                [new ProjectReference("Consumer", manifest)], new DiagnosticBag(),
                (_, _) => configuration, _ => candidates);
            return Assert.Single(resolution).SourcePaths;
        }
    }

    private sealed class MemoryFiles(Dictionary<string, byte[]> content) : IGeneratorFiles
    {
        public Dictionary<string, byte[]> Content { get; } = content;

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
            return Content.Keys.Where(path => SourcePathSelection.IsWithin(path, folder));
        }
    }
}