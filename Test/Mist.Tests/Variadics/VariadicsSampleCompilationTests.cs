// Feature: variadics, task 22.3: verify the Variadics sample compiles cleanly
// in-process through the classic pipeline.
//
// **Validates: Requirement 18.6**
//
// Reads `samples/Variadics/Program.mist`, compiles it in-process via the shared
// classic CompilerDriver (VariadicsCompilerFixture), and asserts zero errors AND
// zero warnings per the zero-warnings policy. Does not spawn a process to run it.
//
// ZERO external process spawns.

using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Verifies that the Variadics sample application (Requirement 18 Proof Code)
/// compiles cleanly in-process through the classic pipeline with zero errors
/// and zero warnings. No process is spawned.
/// </summary>
public sealed class VariadicsSampleCompilationTests
{
    [Fact]
    public void VariadicsSample_CompilesWithZeroErrorsAndWarnings()
    {
        var source = ReadSampleSource();

        var result = VariadicsCompilerFixture.Compile(
            source,
            outputType: ProjectOutputType.Executable,
            projectName: "Variadics",
            globalUsings: "Standard");

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var warnings = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"Expected zero errors but got {errors.Count}:\n{FormatDiagnostics(errors)}");

        Assert.True(
            warnings.Count == 0,
            $"Expected zero warnings but got {warnings.Count}:\n{FormatDiagnostics(warnings)}");
    }

    /// <summary>
    /// Locates and reads the <c>samples/Variadics/Program.mist</c> file from the
    /// repository root.
    /// </summary>
    private static string ReadSampleSource()
    {
        var repoRoot = FindRepoRoot();
        var samplePath = Path.Combine(repoRoot, "samples", "Variadics", "Program.mist");

        Assert.True(
            File.Exists(samplePath),
            $"Variadics sample not found at {samplePath}");

        return File.ReadAllText(samplePath);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        dir = Directory.GetCurrentDirectory();
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (Mist.slnx)");
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(d => $"  {d.Id.Value} [{d.Stage}]: {d.Message}"));
    }
}