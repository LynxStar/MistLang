// Task 13.5 of the drop-semantics spec: integration test for exception propagation through Drop().
// Validates that when a faulted Task<T> is auto-dropped (awaited at scope exit), the exception
// propagates correctly — the program terminates with a non-zero exit code and the exception
// message appears in stderr.
//
// **Validates: Requirements 7.8**
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
/// Integration test that compiles a Mist program where a faulted Task&lt;T&gt; is
/// auto-dropped at scope exit, verifying that the exception propagates.
///
/// The program creates an async method that faults (division by zero), and the
/// Task local reaches scope exit without explicit await. Auto-drop fires await,
/// which propagates the DivideByZeroException.
///
/// Uses the batched approach per test-process-spawning policy:
/// 1 in-process compile + 1 process spawn to run the output DLL.
///
/// **Validates: Requirements 7.8**
/// </summary>
public sealed class ExceptionPropagationDropIntegrationTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public ExceptionPropagationDropIntegrationTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_exc_drop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that when a faulted Task&lt;T&gt; is auto-dropped at scope exit,
    /// the exception propagates — the program exits with a non-zero exit code
    /// and stderr contains evidence of the DivideByZeroException.
    ///
    /// The Mist program:
    /// 1. Defines an async method that performs division by zero (faults the task)
    /// 2. Calls the async method — the Task&lt;int&gt; local reaches scope exit
    /// 3. Auto-drop fires await, which propagates the exception
    /// 4. Prints a "before" marker to prove the task was created
    /// 5. The exception terminates the program before any "after" marker
    ///
    /// **Validates: Requirements 7.8**
    /// </summary>
    [Fact]
    public void FaultedTask_AutoDrop_PropagatesException()
    {
        // Arrange: a Mist program where an async method faults and the task is auto-dropped.
        // The auto-drop mechanism (await at scope exit) should propagate the exception.
        const string mistSource = """
            using Standard;

            namespace TestProgram;

            public class Program
            {
                public static async int FaultyComputation(int x)
                {
                    int result = x / 0;
                    return result;
                }

                public static void Main(string[] args)
                {
                    Console.WriteLine("before-fault");
                    var task = async FaultyComputation(42);
                    Console.WriteLine("task-created");
                }
            }
            """;

        var sourcePath = Path.Combine(_outputDir, "Program.mist");
        File.WriteAllText(sourcePath, mistSource);

        var binDir = Path.Combine(_outputDir, "bin");
        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: binDir,
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        // Act: compile in-process
        var result = SharedDriver.Compile(options);

        // Assert: compilation succeeds
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
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(30_000), "Process timed out after 30 seconds");

        // Assert: program terminated with non-zero exit code (exception propagated)
        Assert.True(process.ExitCode != 0,
            $"Expected non-zero exit code (exception should propagate), but got 0.\nStdout:\n{stdout}\nStderr:\n{stderr}");

        // Assert: stdout shows the program reached the point before the fault
        Assert.Contains("before-fault", stdout);

        // Assert: stderr contains evidence of DivideByZeroException propagation
        var combinedOutput = stdout + stderr;
        Assert.True(
            combinedOutput.Contains("DivideByZero") || combinedOutput.Contains("Divide by zero"),
            $"Expected DivideByZeroException evidence in output.\nStdout:\n{stdout}\nStderr:\n{stderr}");
    }
}