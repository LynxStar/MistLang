using System.Collections.Immutable;
using System.Xml.Linq;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

/// <summary>
/// Tier 4, the boundary from the driver's project options to what the pipeline emits
/// for a library: no entry point demanded, top-level statements refused as Mist's own error rather
/// than Roslyn's, and the test SDK's generated entry point suppressed exactly when the sources carry
/// a Main of their own. The project file is read back because nothing in the compiler does, and the
/// suppression is invisible until `dotnet test` either finds two entry points or none.
/// </summary>
[Trait("Tier", "4")]
public sealed class LibraryOutputEmissionTests : IDisposable
{
    private readonly string scratchDir;

    public LibraryOutputEmissionTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-libout-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    private string CreateProject(string name, string manifest, string source)
    {
        var projectDir = Path.Combine(scratchDir, name);
        _ = Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "mist.toml"), manifest);
        File.WriteAllText(Path.Combine(projectDir, "Program.mist"), source);
        return projectDir;
    }

    private static CompilerResult Compile(string projectDir, ProjectOutputType outputType, string? entryPoint = null)
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
            EntryPoint: entryPoint,
            OutputType: outputType));
    }

    private static XDocument ProjectFile(string projectDir)
    {
        return XDocument.Load(Path.Combine(projectDir, "out", $"{Path.GetFileName(projectDir)}.csproj"));
    }

    private static string Errors(CompilerResult result)
    {
        return string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    [Fact]
    public void ALibraryWithNoMain_CompilesAndLeavesTheTestSdkEntryPointAlone()
    {
        var lib = CreateProject(
            "Lib",
            """
            name = "Lib"
            output_type = "library"
            """,
            """
            namespace Lib;

            public class Probe
            {
                public static int Answer() { return 42; }
            }
            """);

        var result = Compile(lib, ProjectOutputType.Library);

        Assert.True(result.Succeeded, Errors(result));
        var project = ProjectFile(lib);
        Assert.Equal("Library", project.Descendants("OutputType").Single().Value);
        Assert.Empty(project.Descendants("GenerateProgramFile"));
    }

    [Fact]
    public void ALibraryComposingAnExecutable_SuppressesTheTestSdkEntryPoint()
    {
        _ = CreateProject(
            "App",
            """
            name = "App"
            """,
            """
            namespace App;

            public class Program
            {
                public static void Main() { }
            }
            """);
        var lib = CreateProject(
            "Composed",
            """
            name = "Composed"
            output_type = "library"

            [references]
            App = { path = "../App" }
            """,
            """
            namespace Composed;

            public class Probe
            {
                public static int Answer() { return 1; }
            }
            """);

        var result = Compile(lib, ProjectOutputType.Library);

        Assert.True(result.Succeeded, Errors(result));
        Assert.Equal("false", ProjectFile(lib).Descendants("GenerateProgramFile").Single().Value);
    }

    [Fact]
    public void ALibraryWithTopLevelStatements_IsRefused()
    {
        var top = CreateProject(
            "Top",
            """
            name = "Top"
            output_type = "library"
            entry_point = "top-level"
            """,
            """
            using Standard;

            namespace Top;

            Console.WriteLine("top");
            """);

        var result = Compile(top, ProjectOutputType.Library, entryPoint: "top-level");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Id.Value == "MIST_EP_0008");
    }
}