using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Tests.Backend;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Driver;

/// <summary>
/// The seam an external assembly crosses: from the driver's options or a manifest's <c>[references]</c>
/// into the resolved paths the ambient provider and Roslyn read, and into the pipeline's record of
/// external assemblies and composed projects. The fixture assembly is written with
/// <see cref="SyntheticAssembly"/>, so no MSBuild runs; the provider projects that one assembly alone.
/// </summary>
[Trait("Tier", "4")]
public sealed class CompilerDriverExternalAssemblyTests : IDisposable
{
    private const string Program = """
        namespace App;

        public class Program
        {
            public static void Main()
            {
                var widget = new Widgets.Widget();
                var answer = Widgets.Widget.Answer;
            }
        }
        """;

    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), "mist-external-assembly-" + Guid.NewGuid().ToString("N"));
    private PipelineInput? observed;

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            Directory.Delete(scratchDir, recursive: true);
        }
    }

    [Fact]
    public void AnAssemblyHandedToTheDriver_IsBoundAndCompiledAgainst()
    {
        var fixture = SyntheticAssembly.Write(Path.Combine(scratchDir, "fixture"), "Widgets", "Widgets", "Widget");
        var app = CreateProject("""name = "App" """);

        var result = Compile(app, assemblyPaths: [fixture]);

        Assert.True(result.Succeeded, Errors(result));
        Assert.NotNull(observed);
        Assert.Contains(fixture, observed.ResolvedAssemblyPaths);
        Assert.Empty(observed.ExternalAssemblies);
        Assert.Equal<ComposedProject>([new ComposedProject("App", Path.Combine(app, ProjectFileParser.FileName))], observed.ComposedProjects);
    }

    [Fact]
    public void AnAssemblyNamedInReferences_ReachesThePipelineAsAnExternalAssembly()
    {
        var fixture = SyntheticAssembly.Write(Path.Combine(scratchDir, "libs"), "Widgets", "Widgets", "Widget");
        var app = CreateProject("""
            name = "App"

            [references]
            Widgets = { path = "../libs/Widgets.dll" }
            """);

        var result = Compile(app, assemblyPaths: []);

        Assert.True(result.Succeeded, Errors(result));
        Assert.NotNull(observed);
        var external = Assert.Single(observed.ExternalAssemblies);
        Assert.Equal(("Widgets", fixture, ReferenceKind.Assembly), (external.Name, external.Path, external.Kind));
        Assert.Empty(external.Closure);
        Assert.Contains(fixture, observed.ResolvedAssemblyPaths);
    }

    private string CreateProject(string manifest)
    {
        var projectDir = Path.Combine(scratchDir, "App");
        _ = Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), manifest);
        File.WriteAllText(Path.Combine(projectDir, "Program.mist"), Program);
        return projectDir;
    }

    private CompilerResult Compile(string projectDir, ImmutableArray<string> assemblyPaths)
    {
        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser(), ambientTypes: input =>
        {
            observed = input;
            return new FixtureTypes(AmbientProviderTestHelper.ProviderOver(input.Diagnostics, [.. input.ResolvedAssemblyPaths]));
        });

        return driver.Compile(new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: ImmutableArray.Create(Path.Combine(projectDir, "Program.mist")),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: "App",
            NamespaceRoot: "App")
        {
            AssemblyPaths = assemblyPaths,
        });
    }

    private static string Errors(CompilerResult result)
    {
        return string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    private sealed class FixtureTypes(AmbientTypeProviderV2 provider) : IAmbientTypeSource
    {
        public void BuildAndRegister(ProgramModelClass programModel)
        {
            provider.BuildAndRegister(programModel);
        }

        public Type? FindClrType(string fullName)
        {
            return provider.FindClrType(fullName) ?? StubAmbientTypeSource.Instance.FindClrType(fullName);
        }

        public void Dispose()
        {
            provider.Dispose();
        }
    }
}