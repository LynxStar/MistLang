using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Driver.Generators;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "4")]
public sealed class GeneratorDriverSeamTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mist-generator-seam-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ManifestToHostToCompositionCompilesAgainstGeneratedTypes()
    {
        var consumer = Project("Consumer", """
            name = "Consumer"
            output_type = "library"
            [source_generators]
            G = { path = "../G", inputs = ["input.txt"] }
            """);
        File.WriteAllText(Path.Combine(consumer, "input.txt"), "input");
        var source = Path.Combine(consumer, "Consumer.mist");
        File.WriteAllText(source, "namespace Consumer; public class Use { public static int Read() { return GeneratedCode.Generated.Value(); } }");
        _ = Project("G", """
            name = "G"
            project_type = "source_generator"
            """);
        var calls = 0;
        var services = new GeneratorServices
        {
            Toolchain = (_, _) => "seam-toolchain",
            Compile = (_, staging, _) =>
            {
                var artifact = Path.Combine(staging, "G.dll");
                File.WriteAllBytes(artifact, []);
                return artifact;
            },
            Run = invocation =>
            {
                calls++;
                File.WriteAllText(Path.Combine(invocation.WorkingDirectory, "Generated.mist"),
                    "namespace GeneratedCode; public class Generated { public static int Value() { return 42; } }");
                File.WriteAllText(Path.Combine(invocation.WorkingDirectory, "outputs.txt"), "Generated.mist");
                return new DotnetOutcome(0, "captured", "", false);
            }
        };
        PipelineInput? observed = null;
        var driver = new CompilerDriver(new MistLexer(), new MistParser(), ambientTypes: input =>
        {
            observed = input;
            return StubAmbientTypeSource.Instance;
        });
        var result = driver.Compile(new CompilerOptions(consumer, [source], BackendId.CSharp,
            Path.Combine(consumer, "out"), Path.Combine(consumer, "bin"), "Consumer", "Consumer", OutputType: ProjectOutputType.Library)
        {
            GeneratorServices = services
        });
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(1, calls);
        Assert.NotNull(observed);
        var generated = Assert.Single(observed.Compilation.Units, unit => unit.Source.Path.EndsWith("Generated.mist", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(consumer, "out", "generated", "G", "Generated.mist"), generated.Source.Path);
        Assert.Contains(observed.ComposedProjects, project => project.Name == "Consumer.Generated.G");
        Assert.NotNull(result.PrimaryOutputArtifactPath);
    }

    [Fact]
    public void ManifestToReferenceLoaderPreservesCSharpAndAssemblyReferencesWithoutGenerators()
    {
        var bridge = Path.Combine(root, "Bridge");
        _ = Directory.CreateDirectory(bridge);
        var project = Path.Combine(bridge, "Bridge.csproj");
        File.WriteAllText(project, "<Project />");
        var bridgeDll = Path.Combine(bridge, "Bridge.dll");
        File.WriteAllBytes(bridgeDll, []);
        var widget = Path.Combine(root, "Widget.dll");
        File.WriteAllBytes(widget, []);
        var consumer = Project("Consumer", """
            name = "Consumer"
            [references]
            Bridge = { path = "../Bridge/Bridge.csproj" }
            Widget = { path = "../Widget.dll" }
            """);
        var diagnostics = new DiagnosticBag();
        var configuration = ProjectFileParser.Parse(consumer, diagnostics)!;
        DotnetOutcome run(DotnetInvocation invocation)
        {
            return new DotnetOutcome(0, invocation.Arguments.StartsWith("msbuild", StringComparison.Ordinal) ? bridgeDll : "", "", false);
        }

        var ordinary = ProjectReferenceResolver.ResolveAll(consumer, configuration.References, diagnostics);
        var (expected, count) = CSharpProjectBuilder.BuildAll(ordinary.ExternalReferences, "net10.0", "", diagnostics, run);
        var actual = ProjectReferenceLoader.Load(consumer, configuration, diagnostics, NullProgressReporter.Instance,
            ImmutableDictionary<string, ImmutableArray<ResolvedProjectReference>>.Empty,
            new GeneratorServices { Run = run, Toolchain = (_, _) => throw new InvalidOperationException("no generator") });
        Assert.Equal(count, actual.BuiltCSharpProjectCount);
        Assert.Equal(expected.Select(item => (item.Name, item.Path, item.Kind)), actual.ExternalAssemblies.Select(item => (item.Name, item.Path, item.Kind)));
        Assert.Empty(diagnostics.Admitted);
    }

    private string Project(string name, string manifest)
    {
        var folder = Path.Combine(root, name);
        _ = Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mist.toml"), manifest);
        return folder;
    }

    public void Dispose()
    {
        if(Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}