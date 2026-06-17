// Feature: definite-assignment, Property 5: Initializer construction totality
//
// For any element type T and size N, `new Array<T, N>(fn)` produces an array `a`
// such that `a[i] == fn(i)` for all `i ∈ [0, N)`, with no slot observable as null
// or unassigned.
//
// Process-spawning: batched — generate N scenarios, build ONE program that prints
// every element, compile once, run once, compare line-by-line. Single `dotnet`
// spawn for the whole property.
//
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 6.1, 6.2**

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Batched property-based test for Property 5 of the definite-assignment spec:
/// Initializer construction totality.
///
/// Generates deterministic scenarios of (elementType, size, fn), builds a single
/// Mist program that prints each element of each array, compiles once, runs once,
/// and compares every output line to expected values.
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 6.1, 6.2**
/// </summary>
public sealed class InitializerArrayConstructionPropertyTests : IDisposable
{
    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public InitializerArrayConstructionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_init_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario definitions ────────────────────────────────────────────

    private enum ElementTypeKind
    {
        Int,
        String,
        UserClass,
    }

    private enum FnKind
    {
        Identity,      // i => i (for int)
        Constant,      // i => "hello" (for string)
        Computed,      // i => i * 10 (for int)
        IndexString,   // i => $"item-{i}" (for string)
        ClassInstance,  // i => new Item(i) (for user class)
    }

    private sealed record Scenario(
        int Index,
        ElementTypeKind ElementType,
        int Size,
        FnKind Fn)
    {
        public override string ToString() =>
            $"Scenario({Index}, {ElementType}, Size={Size}, {Fn})";
    }

    // ─── Scenario generation ─────────────────────────────────────────────

    private static IReadOnlyList<Scenario> GenerateScenarios()
    {
        var scenarios = new List<Scenario>();
        var index = 0;

        // Int scenarios with various sizes and fn kinds
        foreach (var size in new[] { 1, 2, 3, 5 })
        {
            scenarios.Add(new Scenario(index++, ElementTypeKind.Int, size, FnKind.Identity));
            scenarios.Add(new Scenario(index++, ElementTypeKind.Int, size, FnKind.Computed));
        }

        // String scenarios
        foreach (var size in new[] { 1, 2, 3, 5 })
        {
            scenarios.Add(new Scenario(index++, ElementTypeKind.String, size, FnKind.Constant));
            scenarios.Add(new Scenario(index++, ElementTypeKind.String, size, FnKind.IndexString));
        }

        // UserClass scenarios
        foreach (var size in new[] { 1, 2, 3, 5 })
        {
            scenarios.Add(new Scenario(index++, ElementTypeKind.UserClass, size, FnKind.ClassInstance));
        }

        return scenarios;
    }

    // ─── Expected value computation ──────────────────────────────────────

    private static string ComputeExpectedValue(Scenario scenario, int elementIndex)
    {
        return (scenario.ElementType, scenario.Fn) switch
        {
            (ElementTypeKind.Int, FnKind.Identity) => elementIndex.ToString(),
            (ElementTypeKind.Int, FnKind.Computed) => (elementIndex * 10).ToString(),
            (ElementTypeKind.String, FnKind.Constant) => "hello",
            (ElementTypeKind.String, FnKind.IndexString) => $"item-{elementIndex}",
            (ElementTypeKind.UserClass, FnKind.ClassInstance) => $"Item({elementIndex})",
            _ => throw new InvalidOperationException(
                $"Unexpected scenario combination: {scenario.ElementType}, {scenario.Fn}")
        };
    }

    // ─── Mist program builder ────────────────────────────────────────────

    private static string BuildBatchedProgram(IReadOnlyList<Scenario> scenarios)
    {
        var body = new StringBuilder();

        foreach (var s in scenarios)
        {
            var arrayExpr = BuildArrayExpression(s);
            var elemType = GetMistTypeName(s.ElementType);
            var varName = $"arr_{s.Index}";

            body.AppendLine($"        var {varName} = {arrayExpr};");

            for (var i = 0; i < s.Size; i++)
            {
                var printExpr = BuildPrintExpression(s, varName, i);
                body.AppendLine($"        Console.WriteLine({printExpr});");
            }
        }

        return $$"""
            using Standard;

            namespace TestProgram;

            public class Item
            {
                public int Value;

                public Item(int value)
                {
                    this.Value = value;
                }

                public string Describe()
                {
                    return $"Item({Value})";
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
            {{body}}    }
            }
            """;
    }

    private static string GetMistTypeName(ElementTypeKind kind) => kind switch
    {
        ElementTypeKind.Int => "int",
        ElementTypeKind.String => "string",
        ElementTypeKind.UserClass => "Item",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string BuildArrayExpression(Scenario scenario)
    {
        var elemType = GetMistTypeName(scenario.ElementType);
        var fnExpr = BuildFnExpression(scenario);
        return $"new Array<{elemType}, {scenario.Size}>({fnExpr})";
    }

    private static string BuildFnExpression(Scenario scenario) =>
        (scenario.ElementType, scenario.Fn) switch
        {
            (ElementTypeKind.Int, FnKind.Identity) => "i => i",
            (ElementTypeKind.Int, FnKind.Computed) => "i => i * 10",
            (ElementTypeKind.String, FnKind.Constant) => "i => \"hello\"",
            (ElementTypeKind.String, FnKind.IndexString) => "i => $\"item-{i}\"",
            (ElementTypeKind.UserClass, FnKind.ClassInstance) => "i => new Item(i)",
            _ => throw new InvalidOperationException(
                $"Unexpected fn kind for element type: {scenario.ElementType}, {scenario.Fn}")
        };

    private static string BuildPrintExpression(Scenario scenario, string varName, int elementIndex) =>
        scenario.ElementType switch
        {
            ElementTypeKind.Int => $"{varName}[{elementIndex}]",
            ElementTypeKind.String => $"{varName}[{elementIndex}]",
            ElementTypeKind.UserClass => $"{varName}[{elementIndex}].Describe()",
            _ => throw new ArgumentOutOfRangeException()
        };

    // ─── Compile and run helper ──────────────────────────────────────────

    private (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var iterDir = Path.Combine(_outputDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iterDir);

        try
        {
            var sourcePath = Path.Combine(iterDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var binDir = Path.Combine(iterDir, "bin");
            var options = new CompilerOptions(
                ProjectFolder: iterDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: iterDir,
                BinDirectory: binDir,
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            if (!result.Succeeded || result.PrimaryOutputArtifactPath is null)
            {
                var diagnosticText = string.Join("\n",
                    result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"));
                return (-1, "", $"Compilation failed:\n{diagnosticText}");
            }

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
            if (process is null)
                return (-1, "", "Failed to start dotnet process");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return (-1, "", "Process timed out after 30 seconds");
            }

            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        finally
        {
            try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 5: Initializer construction totality.
    ///
    /// For any element type T and size N, <c>new Array&lt;T, N&gt;(fn)</c> produces
    /// an array a such that a[i] == fn(i) for all i ∈ [0, N), with no slot
    /// observable as null or unassigned.
    ///
    /// Batched: 20 deterministic scenarios → one program → one compile → one run.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 6.1, 6.2**
    /// </summary>
    [Fact]
    public void InitializerConstruction_ProducesCorrectElementValues()
    {
        var scenarios = GenerateScenarios();
        var mistSource = BuildBatchedProgram(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);

        // Compute total expected lines
        var expectedLineCount = scenarios.Sum(s => s.Size);
        Assert.True(lines.Length == expectedLineCount,
            $"Expected {expectedLineCount} output lines but got {lines.Length}.\n" +
            $"Stdout:\n{stdout}");

        // Compare line by line
        var lineIndex = 0;
        foreach (var scenario in scenarios)
        {
            for (var i = 0; i < scenario.Size; i++)
            {
                var expected = ComputeExpectedValue(scenario, i);
                var actual = lines[lineIndex].TrimEnd('\r');
                Assert.True(expected == actual,
                    $"Failed at {scenario}, element index {i}:\n" +
                    $"Expected: '{expected}'\nActual:   '{actual}'\n" +
                    $"Line index: {lineIndex}");
                lineIndex++;
            }
        }
    }
}
