// Task 18.3: Integration test for the StackOnlyTypes sample's exact output.
//
// Compiles samples/StackOnlyTypes/Program.mist once in-process via the shared
// CompilerDriver, then runs the compiled output DLL and asserts stdout matches
// the Requirement 13 Expected output exactly (line by line).
//
// **Validates: Requirements 13.2, 13.3, 13.7, 13.8**
// Spawns exactly 1 external process per test run.

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Integration test that compiles the StackOnlyTypes sample project in-process
/// via <see cref="CompilerDriver"/>, runs the compiled output DLL, and verifies
/// that stdout matches the Requirement 13 Expected output exactly (line by line).
///
/// This proves the entire stack-only types feature works end-to-end:
/// <list type="bullet">
///   <item>stackalloc int[n] parses, type-checks as Span&lt;int&gt;, and emits C# stackalloc</item>
///   <item>Span&lt;int&gt; indexer write/read, Length property</item>
///   <item>Span&lt;int&gt; foreach via intrinsic Enumerator/GetEnumerator</item>
///   <item>Span&lt;T&gt;.Slice returns aliasing view over same memory</item>
///   <item>ReadOnlySpan&lt;int&gt; target-typing with zero-initialized memory</item>
///   <item>Stampable user struct (Point) as stackalloc element type</item>
///   <item>System.Span&lt;int&gt; from int[].AsSpan() unifies with Standard.Span&lt;int&gt;</item>
///   <item>Span argument passing (frame-safe, no escape)</item>
/// </list>
///
/// Uses the batched approach per test-process-spawning policy:
/// 1 in-process compile + 1 process spawn to run the output DLL.
///
/// **Validates: Requirements 13.2, 13.3, 13.7, 13.8**
/// </summary>
public sealed class StackOnlyTypesSampleTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// The exact expected output from Requirement 13.
    /// Each line must match stdout from the compiled sample exactly.
    /// </summary>
    private static readonly string[] ExpectedOutput =
    [
        "=== StackOnlyTypes Sample ===",
        "--- stackalloc Span ---",
        "buffer.Length=4",
        "buffer[0]=10",
        "buffer[3]=40",
        "total=100",
        "--- Slice Aliasing ---",
        "mid.Length=2",
        "mid[0]=20",
        "mid[1]=30",
        "buffer[1]=99",
        "--- ReadOnlySpan ---",
        "ro.Length=3",
        "ro[0]=0",
        "--- stackalloc Struct Element ---",
        "points[0].x=1",
        "points[1].y=4",
        "--- Ambient AsSpan Identity ---",
        "view.Length=3",
        "view[2]=9",
        "--- Span Argument ---",
        "SumSpan=179",
        "=== StackOnlyTypes Done ===",
    ];

    /// <summary>
    /// Compiles the StackOnlyTypes sample in-process, runs it once, and asserts
    /// the stdout matches the Requirement 13 Expected output line by line.
    /// </summary>
    [Fact]
    public void StackOnlyTypesSample_CompilesAndProducesExactExpectedOutput()
    {
        // Arrange: locate the sample directory
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "StackOnlyTypes");
        var sourcePath = Path.Combine(sampleDir, "Program.mist");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(sourcePath), $"Sample source not found: {sourcePath}");

        // Use a temp directory for output to avoid polluting the sample folder
        var outputDir = Path.Combine(Path.GetTempPath(), "mist_stack_only_types_" + Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(outputDir, "bin");
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(binDir);

        try
        {
            // Act: compile in-process via CompilerDriver.Compile()
            var options = new CompilerOptions(
                ProjectFolder: sampleDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: outputDir,
                BinDirectory: binDir,
                ProjectName: "StackOnlyTypes",
                NamespaceRoot: "StackOnlyTypes",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            // Assert: compilation produced zero errors and zero warnings (R13.7)
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

            // Act: run the compiled DLL — exactly 1 process spawn
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

            // Assert: exit code 0 (R13.8)
            Assert.True(process.ExitCode == 0,
                $"Program failed (exit code {process.ExitCode}).\nStderr:\n{stderr}\nStdout:\n{stdout}");

            // Assert: stdout matches expected output exactly, line by line (R13.3)
            var actualLines = stdout.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

            Assert.Equal(ExpectedOutput.Length, actualLines.Length);
            for (var i = 0; i < ExpectedOutput.Length; i++)
            {
                Assert.True(ExpectedOutput[i] == actualLines[i],
                    $"Line {i + 1} mismatch.\n  Expected: \"{ExpectedOutput[i]}\"\n  Actual:   \"{actualLines[i]}\"");
            }
        }
        finally
        {
            // Cleanup temp directory
            if (Directory.Exists(outputDir))
            {
                try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (Mist.slnx)");
    }
}
