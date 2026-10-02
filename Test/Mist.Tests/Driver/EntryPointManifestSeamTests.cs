using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// The seam the entry-point decision crosses twice: a manifest's <c>entry_point</c> travelling from the
/// driver into the model that is judged, for the compiled project and for a referenced one, and the
/// selection travelling on to Roslyn, which must be left nothing to choose. Each program here is refused
/// or miscompiled if either half is dropped; the decision's own rows are Tier 1.
/// </summary>
[Trait("Tier", "4")]
public sealed class EntryPointManifestSeamTests : IDisposable
{
    private const string TwoMains = """
        namespace {0};

        public class A
        {{
            public static void Main() {{ }}
        }}

        public class B
        {{
            public static void Main() {{ }}
        }}
        """;

    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), "mist-entry-seam-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            Directory.Delete(scratchDir, recursive: true);
        }
    }

    [Fact]
    public void TheCompiledProjectsSetting_ChoosesAmongItsMainsAllTheWayToRoslyn()
    {
        var app = CreateProject("App", "name = \"App\"\nentry_point = \"App.B\"", string.Format(TwoMains, "App"));

        var result = Compile(app, entryPoint: "App.B");

        Assert.True(result.Succeeded, Errors(result));
    }

    [Fact]
    public void AReferencedProjectsSettingAndOutputType_AreReadFromItsOwnManifest()
    {
        _ = CreateProject("Lib", "name = \"Lib\"\nentry_point = \"Lib.B\"", string.Format(TwoMains, "Lib"));
        _ = CreateProject("Kit", "name = \"Kit\"\noutput_type = \"library\"", string.Format(TwoMains, "Kit"));
        var app = CreateProject(
            "App",
            """
            name = "App"

            [references]
            Lib = { path = "../Lib" }
            Kit = { path = "../Kit" }
            """,
            "namespace App; public class Program { public static void Main() { } }");

        var result = Compile(app, entryPoint: null);

        Assert.True(result.Succeeded, Errors(result));
    }

    [Fact]
    public void StatementsWithNoSetting_AreTheEntryPointRoslynFindsByItself()
    {
        var app = CreateProject("App", "name = \"App\"", "namespace App; var answer = 42;");

        var result = Compile(app, entryPoint: null);

        Assert.True(result.Succeeded, Errors(result));
    }

    private string CreateProject(string name, string manifest, string source)
    {
        var projectDir = Path.Combine(scratchDir, name);
        _ = Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "mist.toml"), manifest);
        File.WriteAllText(Path.Combine(projectDir, "Program.mist"), source);
        return projectDir;
    }

    private static CompilerResult Compile(string projectDir, string? entryPoint)
    {
        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser());
        var name = Path.GetFileName(projectDir);

        return driver.Compile(new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: ImmutableArray.Create(Path.Combine(projectDir, "Program.mist")),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: name,
            NamespaceRoot: name,
            EntryPoint: entryPoint));
    }

    private static string Errors(CompilerResult result)
    {
        return string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }
}