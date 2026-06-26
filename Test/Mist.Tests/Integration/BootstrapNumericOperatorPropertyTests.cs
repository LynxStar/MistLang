// Task 44 of the Mist Standard Library spec: property-based test that
// bootstrap numeric operators match C# semantics. Design § *Correctness
// Properties → Property 5*:
//
//   For each bootstrap numeric type N and each operator op legal on N,
//   the generator produces operand pairs (a, b) (filtering b == 0 for
//   / and %). The test compiles and executes a Mist program that
//   evaluates a op b and compares the output to the C# evaluation of
//   the equivalent expression on the backing type.
//
// **Validates: Requirements 12.1, 12.2, 12.3, 12.8**

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Batched property-based tests for Task 44 of the Mist Standard Library spec:
/// bootstrap numeric operators match C# semantics.
///
/// Design § *Correctness Properties → Property 5*.
///
/// The test generates 100 random (a, b, op) scenarios, builds a single Mist
/// program that prints all 100 results (one per line), compiles once, runs
/// once, and compares each output line to the C# reference evaluation.
///
/// **Validates: Requirements 12.1, 12.2, 12.3, 12.8**
/// </summary>
public sealed class BootstrapNumericOperatorPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public BootstrapNumericOperatorPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ─── Operator definitions ────────────────────────────────────────────

    private static readonly string[] ArithmeticOps = { "+", "-", "*", "/", "%" };
    private static readonly string[] ComparisonOps = { "<", ">", "<=", ">=", "==", "!=" };
    private static readonly string[] AllOps = ArithmeticOps.Concat(ComparisonOps).ToArray();

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record NumericOpScenario(int A, int B, string Op)
    {
        public override string ToString()
        {
            return $"({A} {Op} {B})";
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<NumericOpScenario> ScenarioGen =>
        from op in Gen.Elements(AllOps)
        from a in Gen.Choose(-100, 100)
        from b in Gen.Choose(-100, 100)
        where !(op is "/" or "%" && b == 0)
        select new NumericOpScenario(a, b, op);

    // ─── C# reference evaluation ─────────────────────────────────────────

    private static string EvaluateInCSharp(int a, int b, string op)
    {
        return op switch
        {
            "+" => (a + b).ToString(CultureInfo.InvariantCulture),
            "-" => (a - b).ToString(CultureInfo.InvariantCulture),
            "*" => (a * b).ToString(CultureInfo.InvariantCulture),
            "/" => (a / b).ToString(CultureInfo.InvariantCulture),
            "%" => (a % b).ToString(CultureInfo.InvariantCulture),
            "<" => (a < b).ToString(),
            ">" => (a > b).ToString(),
            "<=" => (a <= b).ToString(),
            ">=" => (a >= b).ToString(),
            "==" => (a == b).ToString(),
            "!=" => (a != b).ToString(),
            _ => throw new ArgumentException($"Unknown operator: {op}")
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string FormatLiteral(int value)
    {
        return value < 0 ? $"(0 - {-value})" : value.ToString();
    }

    /// <summary>
    /// Builds a single Mist program whose Main evaluates all scenarios
    /// and prints one result per line.
    /// </summary>
    private static string BuildBatchedProgram(IReadOnlyList<NumericOpScenario> scenarios)
    {
        var body = new StringBuilder();
        for(var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            var isComparison = ComparisonOps.Contains(s.Op);
            var resultType = isComparison ? "bool" : "int";
            var a = FormatLiteral(s.A);
            var b = FormatLiteral(s.B);

            _ = body.AppendLine($"        int a{i} = {a};");
            _ = body.AppendLine($"        int b{i} = {b};");
            _ = body.AppendLine($"        {resultType} r{i} = a{i} {s.Op} b{i};");
            _ = body.AppendLine($"        Console.WriteLine(r{i});");
        }

        return $$"""
            using Standard;

            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
            {{body}}    }
            }
            """;
    }

    /// <summary>
    /// Compiles a Mist source in-process via CompilerDriver, then runs the
    /// produced DLL with <c>dotnet</c> to capture stdout.
    /// </summary>
    private (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var iterDir = Path.Combine(outputDir, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(iterDir);

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

            if(!result.Succeeded || result.PrimaryOutputArtifactPath is null)
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
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if(process is null)
            {
                return (-1, "", "Failed to start dotnet process");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if(!process.WaitForExit(30_000))
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
    /// Property 5: For each bootstrap numeric operator, the Mist program
    /// produces the same output as the C# evaluation. 100 random scenarios
    /// batched into a single compile + single run.
    ///
    /// **Validates: Requirements 12.1, 12.2, 12.3, 12.8**
    /// </summary>
    [Fact]
    public void BootstrapNumericOperators_MatchCSharpSemantics()
    {
        var scenarios = ScenarioGen
            .Sample(IterationCount)
            .ToList();

        var mistSource = BuildBatchedProgram(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        Assert.Equal(scenarios.Count, lines.Length);

        for(var i = 0; i < scenarios.Count; i++)
        {
            var expected = EvaluateInCSharp(scenarios[i].A, scenarios[i].B, scenarios[i].Op);
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed: {scenarios[i]}\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }
}