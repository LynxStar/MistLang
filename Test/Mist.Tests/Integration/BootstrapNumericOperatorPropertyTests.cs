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

using System.Diagnostics;
using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Mist.Tests.Integration;

/// <summary>
/// Property-based tests for Task 44 of the Mist Standard Library spec:
/// bootstrap numeric operators match C# semantics.
///
/// Design § *Correctness Properties → Property 5*.
///
/// For each generated test case the property test:
/// <list type="number">
///   <item>Picks a numeric type (Int32) and an operator.</item>
///   <item>Generates operand pairs (a, b) with small values to avoid
///         overflow, filtering b == 0 for / and %.</item>
///   <item>Creates a Mist program that prints the result of a op b.</item>
///   <item>Compiles and runs the Mist program via the CLI.</item>
///   <item>Compares the output to the C# evaluation of the same
///         expression.</item>
/// </list>
///
/// Runs with <c>MaxTest = 100</c> per Requirement 12.8.
/// </summary>
public class BootstrapNumericOperatorPropertyTests
{
    // ─── Repo root discovery ─────────────────────────────────────────────

    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (Mist.slnx) from AppContext.BaseDirectory.");
    });

    private static string CliProjectPath =>
        Path.Combine(RepoRoot.Value, "Source", "Mist.CLI", "Mist.CLI.csproj");

    // ─── Operator definitions ────────────────────────────────────────────

    private static readonly string[] ArithmeticOps = { "+", "-", "*", "/", "%" };
    private static readonly string[] ComparisonOps = { "<", ">", "<=", ">=", "==", "!=" };
    private static readonly string[] AllOps = ArithmeticOps.Concat(ComparisonOps).ToArray();

    // ─── Scenario record ─────────────────────────────────────────────────

    /// <summary>
    /// A single generated scenario: two Int32 operands and an operator.
    /// </summary>
#pragma warning disable CA1034
    public record NumericOpScenario(int A, int B, string Op)
    {
        public override string ToString() => $"({A} {Op} {B})";
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// Generates <see cref="NumericOpScenario"/> instances with small
    /// operand values (-100 to 100) and filters b == 0 for / and %.
    /// </summary>
    public static Arbitrary<NumericOpScenario> ArbitraryNumericOpScenarios()
    {
        var gen =
            from op in Gen.Elements(AllOps)
            from a in Gen.Choose(-100, 100)
            from b in Gen.Choose(-100, 100)
            where !(op is "/" or "%" && b == 0)
            select new NumericOpScenario(a, b, op);

        return gen.ToArbitrary();
    }

    // ─── C# reference evaluation ─────────────────────────────────────────

    /// <summary>
    /// Evaluates the expression in C# and returns the expected output
    /// string (what Console.WriteLine would print).
    /// </summary>
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

    // ─── Mist program generation ─────────────────────────────────────────

    /// <summary>
    /// Generates a Mist program that evaluates a op b and prints the result.
    /// For comparison operators, the result is a bool printed via
    /// Console.WriteLine. For arithmetic operators, the result is an int.
    /// </summary>
    private static string GenerateMistProgram(NumericOpScenario scenario)
    {
        var isComparison = ComparisonOps.Contains(scenario.Op);
        var resultType = isComparison ? "bool" : "int";

        // Use explicit variable declarations to avoid any ambiguity.
        // Negative literals need parentheses to avoid parse issues with
        // unary minus on the literal vs binary minus.
        var aLiteral = scenario.A < 0 ? $"(0 - {-scenario.A})" : scenario.A.ToString();
        var bLiteral = scenario.B < 0 ? $"(0 - {-scenario.B})" : scenario.B.ToString();

        return $$"""
            using System;

            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                    int a = {{aLiteral}};
                    int b = {{bLiteral}};
                    {{resultType}} result = a {{scenario.Op}} b;
                    Console.WriteLine(result);
                }
            }
            """;
    }

    // ─── CLI invocation ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a temp directory with a mist.toml and Program.mist, invokes
    /// the Mist CLI to compile and run, and returns (exitCode, stdout, stderr).
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);

            // Write mist.toml
            File.WriteAllText(
                Path.Combine(tempDir, "mist.toml"),
                "name = \"TestProgram\"\n");

            // Write Program.mist
            File.WriteAllText(
                Path.Combine(tempDir, "Program.mist"),
                mistSource);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{CliProjectPath}\" -- run \"{tempDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Timeout after 60 seconds
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return (-1, "", "Process timed out after 60 seconds");
            }

            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 5: For each bootstrap numeric type and each operator,
    /// the Mist program produces the same output as the C# evaluation.
    ///
    /// Simplified to Int32 with 5 arithmetic + 6 comparison operators.
    ///
    /// Runs ≥100 iterations per Requirement 12.8.
    ///
    /// **Validates: Requirements 12.1, 12.2, 12.3, 12.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(BootstrapNumericOperatorPropertyTests) })]
    public Property BootstrapNumericOperators_MatchCSharpSemantics(
        NumericOpScenario scenario)
    {
        var mistSource = GenerateMistProgram(scenario);
        var expected = EvaluateInCSharp(scenario.A, scenario.B, scenario.Op);

        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        if (exitCode != 0)
        {
            return false.ToProperty()
                .Label($"Mist program failed to compile/run (exit code {exitCode}).\n" +
                       $"Scenario: {scenario}\n" +
                       $"Stderr:\n{stderr}\n" +
                       $"Source:\n{mistSource}");
        }

        var actual = stdout.Trim().Replace("\r\n", "\n");

        if (actual != expected)
        {
            return false.ToProperty()
                .Label($"Output mismatch for {scenario}.\n" +
                       $"Expected: '{expected}'\n" +
                       $"Actual:   '{actual}'\n" +
                       $"Source:\n{mistSource}");
        }

        return true.ToProperty()
            .Label($"Verified: {scenario} => {expected}");
    }
}
