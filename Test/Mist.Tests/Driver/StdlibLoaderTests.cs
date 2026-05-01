using System.Collections.Immutable;
using System.Linq;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

public sealed class StdlibLoaderTests
{
    /// <summary>
    /// Creates a fresh empty temp directory for the duration of a single test.
    /// </summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mist-stdlib-loader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if(Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; swallow so failures during teardown
                // don't mask the real assertion failure.
            }
        }
    }

    [Fact]
    public void LoadFor_ReturnsAllMistFilesAsStdlibUnits()
    {
        using var temp = new TempDir();

        var fileA = Path.Combine(temp.Path, "Alpha.mist");
        var fileB = Path.Combine(temp.Path, "Beta.mist");
        File.WriteAllText(fileA, "namespace Standard; public class Alpha { }\n");
        File.WriteAllText(fileB, "namespace Standard; public class Beta { }\n");

        // An unrelated file should not be picked up.
        File.WriteAllText(Path.Combine(temp.Path, "README.md"), "ignored");

        var diagnostics = new DiagnosticBag();
        ImmutableArray<CompilationUnit> units = StdlibLoader.LoadFrom(temp.Path, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal(2, units.Length);
        Assert.All(units, unit => Assert.True(unit.IsStdlib, $"Expected IsStdlib=true for {unit.Source.Path}"));

        var paths = units.Select(u => u.Source.Path).ToHashSet();
        Assert.Contains(fileA, paths);
        Assert.Contains(fileB, paths);

        // Contents must round-trip through SourceText unchanged.
        var alpha = units.Single(u => u.Source.Path == fileA);
        Assert.Equal(File.ReadAllText(fileA), alpha.Source.Contents);
    }

    [Fact]
    public void LoadFor_MissingDirectory_EmitsStdlibParseDiagnostic()
    {
        var missing = Path.Combine(
            Path.GetTempPath(),
            "mist-stdlib-loader-missing-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(missing), "Precondition: directory must not exist.");

        var diagnostics = new DiagnosticBag();
        ImmutableArray<CompilationUnit> units = StdlibLoader.LoadFrom(missing, diagnostics);

        Assert.Empty(units);
        var diags = diagnostics.ToImmutableArray();
        Assert.Single(diags);

        var diag = diags[0];
        Assert.Equal(DiagnosticId.StdlibParse, diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(PipelineStage.Driver, diag.Stage);
        Assert.Equal(missing, diag.CompilationUnitPath);
        Assert.Contains(missing, diag.Message);
    }
}
