using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

/// <summary>
/// The driver's own stop decisions before the pipeline runs, and what it hands the pipeline about external
/// assemblies. A stopped compile uses a lexer and parser that throw, so a stop that fails to stop is a failure.
/// </summary>
[Trait("Tier", "4")]
public sealed class CompilerDriverOrchestrationTests : IDisposable
{
    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), "mist-driver-orchestration-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingProgressReporter reporter = new();

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch(IOException) { }
        }
    }

    [Fact]
    public void ARootManifestAlreadyInTheInheritedBuildChain_IsACycleReportedBeforeLoading()
    {
        var manifest = Path.Combine(scratchDir, ProjectFileParser.FileName);
        var outer = Path.Combine(Path.GetTempPath(), "mist-outer-" + Guid.NewGuid().ToString("N"), ProjectFileParser.FileName);
        var inherited = Environment.GetEnvironmentVariable(BuildChain.VariableName);
        Environment.SetEnvironmentVariable(BuildChain.VariableName, BuildChain.Format([outer, manifest]));

        CompilerResult result;
        try
        {
            result = StoppingDriver().Compile(Options(Path.Combine(scratchDir, "nested", "..")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(BuildChain.VariableName, inherited);
        }

        Assert.False(result.Succeeded);
        Assert.Null(result.PrimaryOutputArtifactPath);
        var cycle = Assert.Single(result.Diagnostics);
        Assert.Equal(DriverDiagnostics.ReferenceCycle.Number, cycle.Number);
        Assert.Equal($"Reference cycle: {outer} -> {manifest} -> {manifest}.", cycle.Message);
        Assert.Equal(manifest, cycle.Location.Path);
        Assert.Empty(reporter.Events);
    }

    [Fact]
    public void ARootManifestThatIsNotToml_EndsTheCompileAtLoadingWithItsError()
    {
        _ = Directory.CreateDirectory(scratchDir);
        var manifest = Path.Combine(scratchDir, ProjectFileParser.FileName);
        File.WriteAllText(manifest, "name = \"Probe\"\n[references\n");

        var result = StoppingDriver().Compile(Options(scratchDir));

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(DriverDiagnostics.ProjectTomlError.Number, error.Number);
        Assert.Equal(manifest, error.Location.Path);
        Assert.StartsWith($"Failed to parse project file '{manifest}': ", error.Message);
        Assert.Equal(["start 1", "complete 1"], reporter.Sequence);
        Assert.Empty(reporter.DetailsOf("1"));
    }

    [Fact]
    public void ARuntimeIdentifierTheRestoredGraphLacks_StopsTheCompileBeforeLexing()
    {
        var options = Options(scratchDir) with
        {
            Packages = [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")],
            RuntimeIdentifier = "mist-unknown-rid",
        };

        var result = StoppingDriver().Compile(options);

        Assert.False(result.Succeeded);
        var unknown = Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DriverDiagnostics.RuntimeIdentifierUnknown.Number, unknown.Number);
        Assert.Equal("Runtime identifier 'mist-unknown-rid' is not in the SDK's runtime identifier graph.", unknown.Message);
        Assert.Equal(["start 1", "complete 1"], reporter.Sequence);
        Assert.Equal("runtime identifier mist-unknown-rid", reporter.DetailsOf("1")[^1]);
    }

    [Fact]
    public void AnExternalAssemblyNamedLikeAComposedProjectOrAlreadyResolved_IsRecordedButResolvedAtMostOnce()
    {
        var widgets = SyntheticAssembly.Write(Path.Combine(scratchDir, "libs"), "Widgets", "Widgets", "Widget");
        var stale = SyntheticAssembly.Write(Path.Combine(scratchDir, "stale"), "App", "App", "Stale");
        var app = Path.Combine(scratchDir, "App");
        _ = Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, ProjectFileParser.FileName), """
            name = "App"

            [references]
            Widgets = { path = "../libs/Widgets.dll" }
            App = { path = "../stale/App.dll" }
            """);

        PipelineInput? observed = null;
        var driver = new CompilerDriver(new MistLexer(), new MistParser(), ambientTypes: input =>
        {
            observed = input;
            throw new HandedOff();
        });

        _ = Assert.Throws<HandedOff>(() => driver.Compile(Options(app) with { AssemblyPaths = [widgets] }));

        Assert.NotNull(observed);
        Assert.Equal<string>([widgets], observed.ResolvedAssemblyPaths);
        Assert.Equal(
            [("Widgets", widgets), ("App", stale)],
            observed.ExternalAssemblies.Select(external => (external.Name, external.Path)));
        Assert.Equal<ComposedProject>([new ComposedProject("App", Path.Combine(app, ProjectFileParser.FileName))], observed.ComposedProjects);
    }

    private CompilerDriver StoppingDriver()
    {
        return new CompilerDriver(new UnusedLexer(), new UnusedParser(), reporter, ambientTypes: _ => StubAmbientTypeSource.Instance);
    }

    private static CompilerOptions Options(string projectFolder)
    {
        return new CompilerOptions(
            ProjectFolder: projectFolder,
            SourcePaths: [],
            Backend: BackendId.CSharp,
            OutputDirectory: Path.Combine(projectFolder, "out"),
            BinDirectory: Path.Combine(projectFolder, "bin"),
            ProjectName: "App",
            NamespaceRoot: "App");
    }

    private sealed class HandedOff : Exception;

    private sealed class UnusedLexer : ILexer
    {
        public TokenStream Lex(SourceText source, DiagnosticBag diagnostics)
        {
            throw new InvalidOperationException("The lexer ran.");
        }
    }

    private sealed class UnusedParser : IParser
    {
        public ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics)
        {
            throw new InvalidOperationException("The parser ran.");
        }
    }
}