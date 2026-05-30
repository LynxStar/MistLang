// Feature: extension-method-binding, Task 9: Integration tests for ambient extension methods
// Validates: Requirements 3.1, 3.2, 4.1, 4.2, 5.3, 7.1

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// End-to-end integration tests for ambient extension method binding.
/// Verifies that LINQ extension methods become available with <c>using System.Linq</c>,
/// are unavailable without it, and that chained calls and generic type inference work.
///
/// Uses the batched approach per test-process-spawning policy:
/// 1 in-process compile + 1 process spawn to run the output DLL.
///
/// **Validates: Requirements 3.1, 3.2, 4.1, 4.2, 5.3, 7.1**
/// </summary>
public sealed class AmbientExtensionIntegrationTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public AmbientExtensionIntegrationTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "mist_ext_integ_" + Guid.NewGuid().ToString("N"));
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

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Extension methods available with using System.Linq
    // Validates: Requirements 3.1, 4.1, 5.3
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A Mist program with <c>using System.Linq</c> can call <c>.Where()</c>
    /// on an array and get correct results at runtime.
    /// </summary>
    [Fact]
    public void WithUsingSystemLinq_WhereExtension_CompilesAndProducesCorrectOutput()
    {
        const string mistSource = """
            using Standard;
            using System.Linq;

            namespace TestExtensions;

            public class Program
            {
                public static void Main()
                {
                    int[] numbers = [1, 2, 3, 4, 5, 6];
                    var evens = numbers.Where(n => n % 2 == 0);
                    foreach (int e in evens)
                    {
                        Console.WriteLine($"{e}");
                    }
                }
            }
            """;

        var (result, stdout) = CompileAndRun(mistSource);

        Assert.True(result.Succeeded,
            $"Compilation failed.\nDiagnostics:\n{FormatDiagnostics(result)}");

        var lines = ParseOutputLines(stdout);
        Assert.Equal(["2", "4", "6"], lines);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Extension methods unavailable without using System.Linq
    // Validates: Requirements 3.2, 7.1
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A Mist program WITHOUT <c>using System.Linq</c> that tries to call
    /// <c>.Where()</c> gets a compile error (MIST_UNDEFINED_MEMBER).
    /// </summary>
    [Fact]
    public void WithoutUsingSystemLinq_WhereExtension_ProducesCompileError()
    {
        const string mistSource = """
            using Standard;

            namespace TestExtensions;

            public class Program
            {
                public static void Main()
                {
                    int[] numbers = [1, 2, 3, 4, 5, 6];
                    var evens = numbers.Where(n => n % 2 == 0);
                    Console.WriteLine("should not reach here");
                }
            }
            """;

        var result = CompileOnly(mistSource);

        Assert.False(result.Succeeded,
            "Compilation should have failed without using System.Linq");

        // The compilation should fail with MIST_UNDEFINED_MEMBER for 'Where'.
        var hasUndefinedMember = result.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Id.Value == "MIST_UNDEFINED_MEMBER"
            && d.Message.Contains("Where"));

        Assert.True(hasUndefinedMember,
            $"Expected MIST_UNDEFINED_MEMBER for 'Where' but got:\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Chained extension calls
    // Validates: Requirements 4.1, 5.3
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>numbers.Where(n => n > 0).Select(n => n * 2).First()</c> compiles
    /// and produces correct output.
    /// </summary>
    [Fact]
    public void ChainedExtensionCalls_CompileAndProduceCorrectOutput()
    {
        const string mistSource = """
            using Standard;
            using System.Linq;

            namespace TestExtensions;

            public class Program
            {
                public static void Main()
                {
                    int[] numbers = [1, 2, 3, 4, 5];
                    int result = numbers.Where(n => n > 2).Select(n => n * 10).First();
                    Console.WriteLine($"{result}");
                }
            }
            """;

        var (result, stdout) = CompileAndRun(mistSource);

        Assert.True(result.Succeeded,
            $"Compilation failed.\nDiagnostics:\n{FormatDiagnostics(result)}");

        var lines = ParseOutputLines(stdout);
        Assert.Equal(["30"], lines);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: Generic type inference (Where<T> infers T=int from int[])
    // Validates: Requirements 5.3
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>Where&lt;T&gt;</c> infers T=int from <c>int[]</c> receiver.
    /// Verified by calling Where with a lambda that uses int operations.
    /// </summary>
    [Fact]
    public void GenericTypeInference_WhereOnIntArray_InfersTEqualsInt()
    {
        const string mistSource = """
            using Standard;
            using System.Linq;

            namespace TestExtensions;

            public class Program
            {
                public static void Main()
                {
                    int[] numbers = [10, 20, 30, 40, 50];
                    int first = numbers.Where(n => n > 25).First();
                    Console.WriteLine($"{first}");
                }
            }
            """;

        var (result, stdout) = CompileAndRun(mistSource);

        Assert.True(result.Succeeded,
            $"Compilation failed.\nDiagnostics:\n{FormatDiagnostics(result)}");

        var lines = ParseOutputLines(stdout);
        Assert.Equal(["30"], lines);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 5: Count() parameterless extension on filtered results
    // Validates: Requirements 4.1, 4.2
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>.Count()</c> works on filtered results from <c>.Where()</c>.
    /// </summary>
    [Fact]
    public void CountParameterless_OnFilteredResults_ProducesCorrectOutput()
    {
        const string mistSource = """
            using Standard;
            using System.Linq;

            namespace TestExtensions;

            public class Program
            {
                public static void Main()
                {
                    int[] numbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
                    int count = numbers.Where(n => n > 5).Count();
                    Console.WriteLine($"{count}");
                }
            }
            """;

        var (result, stdout) = CompileAndRun(mistSource);

        Assert.True(result.Succeeded,
            $"Compilation failed.\nDiagnostics:\n{FormatDiagnostics(result)}");

        var lines = ParseOutputLines(stdout);
        Assert.Equal(["5"], lines);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source string in-process and returns the result
    /// without running the output. Used for negative tests.
    /// </summary>
    private CompilerResult CompileOnly(string mistSource)
    {
        var sourcePath = Path.Combine(_outputDir, "Program.mist");
        File.WriteAllText(sourcePath, mistSource);

        var binDir = Path.Combine(_outputDir, "bin");
        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: binDir,
            ProjectName: "TestExtensions",
            NamespaceRoot: "TestExtensions",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Compiles a Mist source string in-process and runs the output DLL.
    /// Returns both the compilation result and the stdout from running.
    /// Uses the batched approach: 1 compile, 1 process spawn.
    /// </summary>
    private (CompilerResult Result, string Stdout) CompileAndRun(string mistSource)
    {
        var sourcePath = Path.Combine(_outputDir, "Program.mist");
        File.WriteAllText(sourcePath, mistSource);

        var binDir = Path.Combine(_outputDir, "bin");
        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: binDir,
            ProjectName: "TestExtensions",
            NamespaceRoot: "TestExtensions",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var result = SharedDriver.Compile(options);

        if (!result.Succeeded || result.PrimaryOutputArtifactPath is null)
            return (result, string.Empty);

        // Run the compiled DLL (1 process spawn)
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = result.PrimaryOutputArtifactPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(30_000), "Process timed out after 30 seconds");
        Assert.True(process.ExitCode == 0,
            $"Program failed (exit code {process.ExitCode}).\nStderr:\n{stderr}");

        return (result, stdout);
    }

    private static string[] ParseOutputLines(string stdout)
    {
        return stdout.TrimEnd().Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToArray();
    }

    private static string FormatDiagnostics(CompilerResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  [{d.Id.Value}] {d.Message}");
        return string.Join(Environment.NewLine, errors);
    }
}
