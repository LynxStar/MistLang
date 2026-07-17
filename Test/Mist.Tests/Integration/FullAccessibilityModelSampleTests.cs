// Task 11.3: Integration test for the FullAccessibilityModel sample's exact output.
//
// Compiles samples/FullAccessibilityModel/ in-process via the shared CompilerDriver,
// then runs the compiled output DLL and asserts stdout matches the Requirement 20
// expected output exactly (line by line).
//
// **Validates: Requirements 20.1, 20.2, 20.3, 20.4, 20.5, 20.6**
// Spawns exactly 1 external process per test method (compile in-process, spawn
// only to run the built output).

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Integration test that compiles the FullAccessibilityModel sample project
/// in-process via <see cref="CompilerDriver"/>, runs the compiled output DLL,
/// and verifies that stdout matches the Requirement 20 expected output exactly
/// (line by line).
///
/// This proves the full accessibility model feature works end-to-end:
/// <list type="bullet">
///   <item><c>protected</c> and <c>internal</c> parse and are enforced as member accessibility</item>
///   <item>Protected field reachable from derived types (positive path)</item>
///   <item>Internal field reachable from any type in the same compilation</item>
///   <item>Private field reachable only inside the declaring type</item>
///   <item>Default (no modifier) member resolves to <c>private</c></item>
///   <item><c>sealed</c> class and struct accepted with no diagnostic</item>
///   <item>Mist structs are inheritable with <c>protected</c>/<c>internal</c> on struct members</item>
///   <item>Derived struct accesses inherited <c>protected</c> and <c>internal</c> members</item>
///   <item>Non-derived type in same compilation accesses <c>internal</c> members</item>
/// </list>
///
/// Uses the batched approach per test-process-spawning policy:
/// 1 in-process compile + 1 process spawn to run the output DLL.
///
/// <para>
/// <b>Known issue:</b> Currently skipped because inherited struct fields
/// (<c>name</c>, <c>magnitude</c>) do not resolve in derived structs
/// (MIST_UNDEFINED_IDENTIFIER). Once struct field resolution across
/// inheritance is fixed, un-skip this test.
/// </para>
/// </summary>
public sealed class FullAccessibilityModelSampleTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// The exact expected output from Requirement 20.
    /// Each line must match stdout from the compiled sample exactly.
    /// </summary>
    private static readonly string[] ExpectedOutput =
    [
        "=== FullAccessibilityModel Mist Sample ===",
        "square: 4 sides, fingerprint 10",
        "sides via same-compilation access: 4",
        "balance after deposit: 125",
        "currency (internal): USD",
        "north: magnitude 1",
        "frozen: value 21, checksum 42",
        "vector name (internal): north",
        "Done!",
    ];

    /// <summary>
    /// Compiles the FullAccessibilityModel sample in-process, runs it once,
    /// and asserts the stdout matches the Requirement 20 expected output
    /// line by line with zero warnings.
    /// </summary>
    [Fact]
    public void FullAccessibilityModelSample_CompilesAndProducesExactExpectedOutput()
    {
        // Arrange: locate the sample directory and discover all .mist files
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "FullAccessibilityModel");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");

        var sourceFiles = Directory.GetFiles(sampleDir, "*.mist");
        Assert.NotEmpty(sourceFiles);

        // Use a temp directory for output to avoid polluting the sample folder
        var outputDir = Path.Combine(Path.GetTempPath(), "mist_full_accessibility_" + Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(outputDir, "bin");
        _ = Directory.CreateDirectory(outputDir);
        _ = Directory.CreateDirectory(binDir);

        try
        {
            // Act: compile in-process via CompilerDriver.Compile()
            var options = new CompilerOptions(
                ProjectFolder: sampleDir,
                SourcePaths: sourceFiles.ToImmutableArray(),
                Backend: new BackendId("csharp"),
                OutputDirectory: outputDir,
                BinDirectory: binDir,
                ProjectName: "FullAccessibilityModel",
                NamespaceRoot: "FullAccessibilityModel",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            // Assert: compilation produced zero errors and zero warnings (Req 20.6)
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            var warnings = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Warning)
                .ToList();

            Assert.True(result.Succeeded,
                $"Compilation failed. Errors:\n{string.Join("\n", errors.Select(d => $"  {d.Id.Value}: {d.Message}"))}");
            Assert.Empty(warnings);
            Assert.NotNull(result.PrimaryOutputArtifactPath);

            // Act: run the compiled DLL -- exactly 1 process spawn
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = result.PrimaryOutputArtifactPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            Assert.NotNull(process);

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            Assert.True(process.WaitForExit(30_000), "Process timed out after 30 seconds");

            // Assert: exit code 0 (Req 20.6)
            Assert.True(process.ExitCode == 0,
                $"Program failed (exit code {process.ExitCode}).\nStderr:\n{stderr}\nStdout:\n{stdout}");

            // Assert: stdout matches expected output exactly, line by line (Req 20.4)
            var actualLines = stdout.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

            Assert.Equal(ExpectedOutput.Length, actualLines.Length);
            for(var i = 0; i < ExpectedOutput.Length; i++)
            {
                Assert.True(ExpectedOutput[i] == actualLines[i],
                    $"Line {i + 1} mismatch.\n  Expected: \"{ExpectedOutput[i]}\"\n  Actual:   \"{actualLines[i]}\"");
            }
        }
        finally
        {
            // Cleanup temp directory
            if(Directory.Exists(outputDir))
            {
                try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Verifies that the FullAccessibilityModel sample produces correct output
    /// when invoked via the AllFeatures orchestrator sample. This confirms
    /// <c>samples/AllFeatures/Program.mist</c> calls
    /// <c>FullAccessibilityModel.Program.RunDemo()</c> and includes the
    /// feature in the full-suite sample run.
    /// </summary>
    [Fact]
    public void AllFeatures_IncludesFullAccessibilityModelOutput()
    {
        // Arrange: locate AllFeatures sample directory
        var repoRoot = FindRepoRoot();
        var allFeaturesDir = Path.Combine(repoRoot, "samples", "AllFeatures");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(allFeaturesDir), $"AllFeatures directory not found: {allFeaturesDir}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        // Act: invoke the Mist CLI to compile and run AllFeatures with --quiet
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{cliProjectPath}\" -v q -- run \"{allFeaturesDir}\" --quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(120_000), "AllFeatures process timed out after 120 seconds");

        // Assert: exit code 0
        Assert.True(process.ExitCode == 0,
            $"AllFeatures failed (exit code {process.ExitCode}).\nStderr:\n{stderr}");

        // Assert: the FullAccessibilityModel output appears in the combined stdout
        var output = stdout.Replace("\r\n", "\n");

        Assert.Contains("=== FullAccessibilityModel Mist Sample ===", output);
        Assert.Contains("square: 4 sides, fingerprint 10", output);
        Assert.Contains("sides via same-compilation access: 4", output);
        Assert.Contains("balance after deposit: 125", output);
        Assert.Contains("currency (internal): USD", output);
        Assert.Contains("north: magnitude 1", output);
        Assert.Contains("frozen: value 21, checksum 42", output);
        Assert.Contains("vector name (internal): north", output);
        Assert.Contains("Done!", output);
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
}