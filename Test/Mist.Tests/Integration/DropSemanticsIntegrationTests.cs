// Task 13.3: Integration test for the DropSemantics sample's exact output.
//
// Compiles samples/DropSemantics/Program.mist once in-process via the shared
// CompilerDriver, then runs the compiled output DLL and asserts stdout matches
// the Requirement 17 Expected output exactly (line by line).
//
// **Validates: Requirements 17.1, 17.2, 17.3, 17.5**
// Spawns at most 1 external process per test run.

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Integration test that compiles the DropSemantics sample project in-process
/// via <see cref="CompilerDriver"/>, runs the compiled output DLL, and verifies
/// that stdout matches the Requirement 17 Expected output exactly (line by line).
///
/// This proves the entire drop semantics feature works end-to-end:
/// <list type="bullet">
///   <item>Linear auto-drop at scope exit (exactly once)</item>
///   <item>Linear drop elision on move and return</item>
///   <item>Linear Drop() with return value capture</item>
///   <item>Reverse declaration order for multiple drops</item>
///   <item>Relevant type drop fires for every reference (idempotent)</item>
///   <item>Affine type drops only in with-blocks</item>
///   <item>Affine type does NOT auto-drop outside with</item>
///   <item>Synthetic drop on containing types (field drop in declaration order)</item>
///   <item>Lending retains drop responsibility on the owner</item>
/// </list>
///
/// Uses the batched approach per test-process-spawning policy:
/// 1 in-process compile + 1 process spawn to run the output DLL.
///
/// **Validates: Requirements 17.1, 17.2, 17.3, 17.5**
/// </summary>
public sealed class DropSemanticsIntegrationTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// The exact expected output from Requirement 17.
    /// Each line must match stdout from the compiled sample exactly.
    /// </summary>
    private static readonly string[] ExpectedOutput =
    [
        "=== Drop Semantics Sample ===",
        "--- 1. Linear: auto-drop at scope exit ---",
        "  Created FileHandle(1)",
        "  FileHandle(1).Drop() called",
        "--- 2. Linear: drop elision on move ---",
        "  Created FileHandle(2)",
        "  Sink consumed FileHandle(2)",
        "  FileHandle(2).Drop() called",
        "--- 3. Linear: drop elision on return ---",
        "  PassThrough FileHandle(3)",
        "  Got back FileHandle(3)",
        "  FileHandle(3).Drop() called",
        "--- 4. Linear: Drop with return value ---",
        "  LinearBox.Drop() called, yielding value",
        "  Got value: 42",
        "  FileHandle(42).Drop() called",
        "--- 5. Linear: reverse drop ordering ---",
        "  Created: 10, 11, 12",
        "  FileHandle(12).Drop() called",
        "  FileHandle(11).Drop() called",
        "  FileHandle(10).Drop() called",
        "--- 6. Relevant: drop fires for every reference ---",
        "  res=conn-1, alias=conn-1",
        "  SharedResource(conn-1).Drop() called",
        "  SharedResource(conn-1).Drop() no-op",
        "--- 7. Affine: only drops in with block ---",
        "  Inside with-block",
        "  AuthToken(abc-123).Drop() called - consumed",
        "--- 8. Affine: no auto-drop outside with ---",
        "  AuthToken(xyz-789) falls out of scope",
        "--- 9. Synthetic drop on containing type ---",
        "  Session created with handles 50 and 51",
        "  FileHandle(50).Drop() called",
        "  FileHandle(51).Drop() called",
        "--- 10. Lending retains drop responsibility ---",
        "  ReadFd sees FileHandle(30)",
        "  Owner still has FileHandle(30)",
        "  FileHandle(30).Drop() called",
        "=== Drop Semantics Done ===",
    ];

    /// <summary>
    /// Compiles the DropSemantics sample in-process, runs it once, and asserts
    /// the stdout matches the Requirement 17 Expected output line by line.
    /// </summary>
    [Fact]
    public void DropSemanticsSample_CompilesAndProducesExactExpectedOutput()
    {
        // Arrange: locate the sample directory
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "DropSemantics");
        var sourcePath = Path.Combine(sampleDir, "Program.mist");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(sourcePath), $"Sample source not found: {sourcePath}");

        // Use a temp directory for output to avoid polluting the sample folder
        var outputDir = Path.Combine(Path.GetTempPath(), "mist_drop_semantics_" + Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(outputDir, "bin");
        _ = Directory.CreateDirectory(outputDir);
        _ = Directory.CreateDirectory(binDir);

        try
        {
            // Act: compile in-process
            var options = new CompilerOptions(
                ProjectFolder: sampleDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: outputDir,
                BinDirectory: binDir,
                ProjectName: "DropSemantics",
                NamespaceRoot: "DropSemantics",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            // Assert: compilation succeeded
            Assert.True(result.Succeeded,
                $"Compilation failed. Diagnostics:\n{string.Join("\n", result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"))}");

            Assert.NotNull(result.PrimaryOutputArtifactPath);

            // Act: run the compiled DLL (1 process spawn)
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
            Assert.True(process.ExitCode == 0,
                $"Program failed (exit code {process.ExitCode}).\nStderr:\n{stderr}\nStdout:\n{stdout}");

            // Assert: stdout matches expected output exactly, line by line
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