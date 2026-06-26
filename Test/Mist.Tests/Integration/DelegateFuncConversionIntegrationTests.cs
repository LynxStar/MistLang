// Task 11.3: Dedicated integration test for the DelegateFuncConversion sample.
//
// Compiles samples/DelegateFuncConversion/Program.mist in-process via the shared
// CompilerDriver, resolves the Microsoft.AspNetCore.App framework reference, then
// runs the compiled output DLL and asserts stdout matches the Requirement 10
// expected output byte-for-byte.
//
// **Validates: Requirements 10.7, 10.9**
// Spawns at most 1 external process per test run (APPROVED single-spawn exception).

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Integration test that compiles the DelegateFuncConversion sample project
/// in-process via <see cref="CompilerDriver"/>, runs the compiled output DLL,
/// and verifies that stdout matches the Requirement 10 expected output exactly.
///
/// This proves the delegate-func conversion feature works end-to-end:
/// <list type="bullet">
///   <item>Lambda to Named_Delegate (Predicate&lt;int&gt; via FindAll)</item>
///   <item>Method group to Named_Delegate (Predicate&lt;int&gt; via FindAll)</item>
///   <item>Lambda to Named_Delegate (Comparison&lt;int&gt; via Sort)</item>
///   <item>func value assigned to Predicate&lt;int&gt; variable</item>
///   <item>Predicate&lt;int&gt; value assigned to func variable</item>
///   <item>func value assigned to System.Delegate variable</item>
///   <item>Direct invocation of a Predicate&lt;int&gt;-typed value</item>
///   <item>Lambda to System.Delegate parameter (ASP.NET Core MapGet pattern)</item>
/// </list>
///
/// Uses the batched approach per test-process-spawning policy:
/// 1 in-process compile + 1 process spawn to run the output DLL.
///
/// **Validates: Requirements 10.7, 10.9**
/// </summary>
public sealed class DelegateFuncConversionIntegrationTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// The exact expected output from Requirement 10.
    /// Each line must match stdout from the compiled sample exactly.
    /// </summary>
    private static readonly string[] ExpectedOutput =
    [
        "=== DelegateFuncConversion Sample ===",
        "positives.Count=3",
        "alsoPositives.Count=3",
        "sortedFirst=5",
        "evens.Count=3",
        "funcFromDelegate(4)=True",
        "baseDelegateType=Func`2",
        "predicate(6)=True",
        "predicate(7)=False",
        "mapGetRegistered=true",
        "=== DelegateFuncConversion Done ===",
    ];

    /// <summary>
    /// Compiles the DelegateFuncConversion sample in-process (resolving the
    /// Microsoft.AspNetCore.App framework reference), runs it once, and asserts
    /// the stdout matches the Requirement 10 expected output line by line.
    /// </summary>
    [Fact]
    public void DelegateFuncConversionSample_CompilesAndProducesExactExpectedOutput()
    {
        // Arrange: locate the sample directory
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "DelegateFuncConversion");
        var sourcePath = Path.Combine(sampleDir, "Program.mist");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(sourcePath), $"Sample source not found: {sourcePath}");

        // Use a temp directory for output to avoid polluting the sample folder
        var outputDir = Path.Combine(Path.GetTempPath(), "mist_delegate_func_" + Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(outputDir, "bin");
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(binDir);

        try
        {
            // Act: compile in-process with the Microsoft.AspNetCore.App framework reference
            var options = new CompilerOptions(
                ProjectFolder: sampleDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: outputDir,
                BinDirectory: binDir,
                ProjectName: "DelegateFuncConversion",
                NamespaceRoot: "DelegateFuncConversion",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                FrameworkReferences: ImmutableArray.Create(
                    new FrameworkReference("Microsoft.AspNetCore.App")));

            var result = SharedDriver.Compile(options);

            // Assert: compilation succeeded
            Assert.True(result.Succeeded,
                $"Compilation failed. Diagnostics:\n{string.Join("\n", result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"))}");

            Assert.NotNull(result.PrimaryOutputArtifactPath);

            // Act: run the compiled DLL (1 process spawn — APPROVED exception)
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
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        dir = Directory.GetCurrentDirectory();
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (Mist.slnx)");
    }
}