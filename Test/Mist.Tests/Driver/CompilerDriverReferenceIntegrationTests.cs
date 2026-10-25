using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

/// <summary>
/// Integration tests for <see cref="CompilerDriver"/> with project references.
/// Validates that invalid references produce correct diagnostics.
///
/// Validates: Requirements 3.1, 3.2.
/// </summary>
[Trait("Tier", "4")]
public sealed class CompilerDriverReferenceIntegrationTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        ambientTypes: _ => StubAmbientTypeSource.Instance);

    private readonly string scratchDir;

    public CompilerDriverReferenceIntegrationTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-ref-integ-" + Guid.NewGuid().ToString("N"));
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

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a project directory with a mist.toml and optional source files.
    /// Returns the absolute path to the project directory.
    /// </summary>
    private string CreateProject(
        string name,
        string? namespaceRoot = null,
        string[]? referencePaths = null,
        Dictionary<string, string>? sourceFiles = null)
    {
        var projectDir = Path.Combine(scratchDir, name);
        _ = Directory.CreateDirectory(projectDir);

        var toml = $"name = \"{name}\"\n";
        if(namespaceRoot is not null)
        {
            toml += $"namespace_root = \"{namespaceRoot}\"\n";
        }

        if(referencePaths is not null)
        {
            toml += "\n[references]\n";
            for(var i = 0; i < referencePaths.Length; i++)
            {
                // Use forward slashes for TOML compatibility
                var escapedPath = referencePaths[i].Replace("\\", "/");
                // Derive a reference name from the directory name
                // If the path ends with mist.toml, use the parent directory name
                var trimmedPath = referencePaths[i].TrimEnd('/', '\\');
                var fileName = Path.GetFileName(trimmedPath);
                var refName = string.Equals(fileName, "mist.toml", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(Path.GetDirectoryName(trimmedPath)!)
                    : fileName;
                toml += $"{refName} = {{ path = \"{escapedPath}\" }}\n";
            }
        }

        File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

        if(sourceFiles is not null)
        {
            foreach(var (fileName, content) in sourceFiles)
            {
                File.WriteAllText(Path.Combine(projectDir, fileName), content);
            }
        }

        return projectDir;
    }

    // ── Invalid reference → MIST_PROJECT_REFERENCE_NOT_FOUND ─

    /// <summary>
    /// Project A declares a reference to a non-existent path. The
    /// compilation should abort with a <c>MIST_PROJECT_REFERENCE_NOT_FOUND</c>
    /// diagnostic.
    /// </summary>
    [Fact]
    public void InvalidReference_ProducesNotFoundDiagnostic()
    {
        var bogusPath = Path.Combine(scratchDir, "NonExistent", "mist.toml");

        var projectA = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [bogusPath],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main()
                        {
                        }
                    }
                    """
            });

        var sourcePaths = Directory.GetFiles(projectA, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectA,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectA, "out"),
            BinDirectory: Path.Combine(projectA, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp");

        var result = SharedDriver.Compile(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics,
            d => d.Number.Value == "MIST_PROJECT_REFERENCE_NOT_FOUND");
    }
}