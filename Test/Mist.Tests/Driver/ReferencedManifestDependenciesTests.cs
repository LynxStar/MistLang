using System.Collections.Immutable;
using System.Xml.Linq;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Tier 4, the boundary from a REFERENCED project's manifest to package resolution (Stage 1.5) and
/// on through the pipeline to the project file it emits.
/// <para>
/// Standalone, a project's own manifest is all resolution ever reads, so a sample declaring the
/// framework its files use is green on its own and unresolved the moment another project
/// references it. The orchestrators are where that was visible, and only as a refusal three stages
/// later. The project file is asserted here too because nothing in the compiler reads it back.
/// </para>
/// </summary>
[Trait("Tier", "4")]
public sealed class ReferencedManifestDependenciesTests : IDisposable
{
    private readonly string scratchDir;

    public ReferencedManifestDependenciesTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-refdeps-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void FrameworkDeclaredByAReferencedProject_ReachesResolutionAndTheProjectFile()
    {
        _ = CreateProject(
            "Lib",
            """
            name = "Lib"

            [framework_references]
            "Microsoft.AspNetCore.App" = true
            """,
            """
            using Standard;

            namespace Lib;

            public class Probe
            {
                public static void Show()
                {
                    var qs = new Microsoft.AspNetCore.Http.QueryString("?id=42");
                    Console.WriteLine($"query={qs.Value}");
                }
            }
            """);

        var appDir = CreateProject(
            "App",
            """
            name = "App"

            [references]
            Lib = { path = "../Lib" }
            """,
            """
            namespace App;

            public class Program
            {
                public static void Main()
                {
                    Lib.Probe.Show();
                }
            }
            """);

        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser());

        var outputDir = Path.Combine(appDir, "out");
        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: appDir,
            SourcePaths: ImmutableArray.Create(Path.Combine(appDir, "Program.mist")),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(appDir, "bin"),
            ProjectName: "App",
            NamespaceRoot: "App"));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"A referenced project's framework reference did not reach resolution:\n{string.Join("\n", errors)}");
        Assert.True(result.Succeeded);

        var project = XDocument.Load(Path.Combine(outputDir, "App.csproj"));
        Assert.Equal(
            ["Microsoft.AspNetCore.App"],
            project.Descendants("FrameworkReference").Select(e => e.Attribute("Include")?.Value));
    }
}