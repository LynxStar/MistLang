// Task 7.6 of the stdlib-ambient-migration spec: integration test for emitter output verification.
// Validates that stdlib types (Console, Math, Convert) emit correct CLR-qualified names
// and produce correct runtime output.
//
// **Validates: Requirements 4.4, 8.1, 8.2, 8.3, 8.4**

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
/// Integration test that compiles a single Mist program using all stdlib types
/// (Console, Math, Convert) with <c>using Standard;</c>, verifies the emitted C#
/// source contains fully-qualified CLR type names, and runs the compiled output
/// to verify correct stdout.
///
/// Uses the batched approach per test-process-spawning policy: 1 in-process compile + 1 process run.
///
/// **Validates: Requirements 4.4, 8.1, 8.2, 8.3, 8.4**
/// </summary>
public sealed class StdlibEmitterIntegrationTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public StdlibEmitterIntegrationTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_stdlib_emit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Compiles a Mist program that exercises all five stdlib methods:
    /// Console.WriteLine, Math.Abs, Math.Max, Math.Min, Convert.ToInt32.
    ///
    /// Verifies:
    /// 1. The emitted C# source contains System.Console.WriteLine, System.Math.Abs,
    ///    System.Math.Max, System.Math.Min, System.Convert.ToInt32
    /// 2. Running the compiled DLL produces the expected stdout output
    ///
    /// **Validates: Requirements 4.4, 8.1, 8.2, 8.3, 8.4**
    /// </summary>
    [Fact]
    public void StdlibTypes_EmitCorrectCLRNames_AndProduceCorrectOutput()
    {
        // Arrange: a single Mist program that calls all 5 stdlib methods.
        // Console.WriteLine only accepts String, so we use string interpolation
        // to convert int results to strings for output.
        const string mistSource = """
            using Standard;

            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                    Console.WriteLine("hello");
                    int absResult = Math.Abs(0 - 42);
                    Console.WriteLine($"abs:{absResult}");
                    int maxResult = Math.Max(10, 20);
                    Console.WriteLine($"max:{maxResult}");
                    int minResult = Math.Min(10, 20);
                    Console.WriteLine($"min:{minResult}");
                    int converted = Convert.ToInt32("99");
                    Console.WriteLine($"conv:{converted}");
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

        // Assert: compilation succeeded
        Assert.True(result.Succeeded,
            $"Compilation failed. Diagnostics:\n{string.Join("\n", result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"))}");

        // Assert: emitted C# source contains fully-qualified CLR type names
        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        Assert.NotEmpty(csFiles);

        var emittedSource = string.Join("\n", csFiles.Select(File.ReadAllText));

        Assert.Contains("System.Console.WriteLine", emittedSource);
        Assert.Contains("System.Math.Abs", emittedSource);
        Assert.Contains("System.Math.Max", emittedSource);
        Assert.Contains("System.Math.Min", emittedSource);
        Assert.Contains("System.Convert.ToInt32", emittedSource);

        // Act: run the compiled DLL (1 process spawn)
        Assert.NotNull(result.PrimaryOutputArtifactPath);

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
        Assert.True(process.ExitCode == 0,
            $"Program failed (exit code {process.ExitCode}).\nStderr:\n{stderr}");

        // Assert: correct stdout output
        var lines = stdout.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        Assert.Equal(5, lines.Length);
        Assert.Equal("hello", lines[0]);
        Assert.Equal("abs:42", lines[1]);    // Math.Abs(0 - 42) == 42
        Assert.Equal("max:20", lines[2]);    // Math.Max(10, 20) == 20
        Assert.Equal("min:10", lines[3]);    // Math.Min(10, 20) == 10
        Assert.Equal("conv:99", lines[4]);   // Convert.ToInt32("99") == 99
    }
}
