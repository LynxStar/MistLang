// Task 46 of the Mist Standard Library spec: property-based test that
// operator round-trip inverse holds. Design § *Correctness Properties →
// Property 6*:
//
//   For each bootstrap numeric type N with both + and - declared,
//   generator produces operand pairs (a, b) filtered so that a + b
//   does not overflow. Assert: Mist expression (a + b) - b evaluates
//   to a.
//
// **Validates: Requirements 12.7, 12.8**

using System.Diagnostics;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Mist.Tests.Integration;

/// <summary>
/// Property-based tests for Task 46 of the Mist Standard Library spec:
/// operator round-trip inverse.
///
/// Design § *Correctness Properties → Property 6*.
///
/// For each generated test case the property test:
/// <list type="number">
///   <item>Picks a numeric type (Int32 or Int64) and operand pair (a, b)
///         filtered so a + b does not overflow.</item>
///   <item>Creates a Mist program that prints (a + b) - b.</item>
///   <item>Compiles and runs the Mist program via the CLI.</item>
///   <item>Asserts the output equals a.</item>
/// </list>
///
/// Runs with <c>MaxTest = 100</c> per Requirement 12.8.
/// </summary>
public class OperatorRoundTripPropertyTests
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

    // ─── Scenario records ────────────────────────────────────────────────

    /// <summary>
    /// A single generated scenario for Int32 round-trip.
    /// </summary>
#pragma warning disable CA1034
    public record Int32RoundTripScenario(int A, int B)
    {
        public override string ToString() => $"Int32: ({A} + {B}) - {B} == {A}";
    }

    /// <summary>
    /// A single generated scenario for Int64 round-trip.
    /// </summary>
    public record Int64RoundTripScenario(long A, long B)
    {
        public override string ToString() => $"Int64: ({A} + {B}) - {B} == {A}";
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates <see cref="Int32RoundTripScenario"/> instances with small
    /// values that won't overflow when added.
    /// </summary>
    public static Arbitrary<Int32RoundTripScenario> ArbitraryInt32RoundTripScenarios()
    {
        // Use values in [-10000, 10000] to ensure a + b stays within int range
        var gen =
            from a in Gen.Choose(-10000, 10000)
            from b in Gen.Choose(-10000, 10000)
            select new Int32RoundTripScenario(a, b);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates <see cref="Int64RoundTripScenario"/> instances with small
    /// values that won't overflow when added.
    /// </summary>
    public static Arbitrary<Int64RoundTripScenario> ArbitraryInt64RoundTripScenarios()
    {
        // Use values in [-10000, 10000] to ensure a + b stays within long range
        var gen =
            from a in Gen.Choose(-10000, 10000)
            from b in Gen.Choose(-10000, 10000)
            select new Int64RoundTripScenario((long)a, (long)b);

        return gen.ToArbitrary();
    }

    // ─── Mist program generation ─────────────────────────────────────────

    /// <summary>
    /// Generates a Mist program that computes (a + b) - b and prints the result.
    /// Uses explicit variable declarations to avoid parse ambiguity with
    /// negative literals.
    /// </summary>
    private static string GenerateInt32Program(Int32RoundTripScenario scenario)
    {
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
                    int sum = a + b;
                    int result = sum - b;
                    Console.WriteLine(result);
                }
            }
            """;
    }

    /// <summary>
    /// Generates a Mist program that computes (a + b) - b for long values.
    /// </summary>
    private static string GenerateInt64Program(Int64RoundTripScenario scenario)
    {
        var aLiteral = scenario.A < 0 ? $"(0 - {-scenario.A})" : scenario.A.ToString();
        var bLiteral = scenario.B < 0 ? $"(0 - {-scenario.B})" : scenario.B.ToString();

        return $$"""
            using System;

            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                    long a = {{aLiteral}};
                    long b = {{bLiteral}};
                    long sum = a + b;
                    long result = sum - b;
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
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_rt_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);

            File.WriteAllText(
                Path.Combine(tempDir, "mist.toml"),
                "name = \"TestProgram\"\n");

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

    // ─── Properties ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 6 (Int32): (a + b) - b == a for Int32 operands.
    ///
    /// Runs ≥100 iterations per Requirement 12.8.
    ///
    /// **Validates: Requirements 12.7, 12.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OperatorRoundTripPropertyTests) })]
    public Property Int32_AddSubtract_RoundTrip(Int32RoundTripScenario scenario)
    {
        var mistSource = GenerateInt32Program(scenario);
        var expected = scenario.A.ToString();

        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        if (exitCode != 0)
        {
            return false.ToProperty()
                .Label($"Mist program failed (exit code {exitCode}).\n" +
                       $"Scenario: {scenario}\n" +
                       $"Stderr:\n{stderr}\n" +
                       $"Source:\n{mistSource}");
        }

        var actual = stdout.Trim().Replace("\r\n", "\n");

        if (actual != expected)
        {
            return false.ToProperty()
                .Label($"Round-trip failed for {scenario}.\n" +
                       $"Expected: '{expected}'\n" +
                       $"Actual:   '{actual}'");
        }

        return true.ToProperty()
            .Label($"Verified: {scenario}");
    }

    /// <summary>
    /// Property 6 (Int64): (a + b) - b == a for Int64 operands.
    ///
    /// Runs ≥100 iterations per Requirement 12.8.
    ///
    /// **Validates: Requirements 12.7, 12.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OperatorRoundTripPropertyTests) })]
    public Property Int64_AddSubtract_RoundTrip(Int64RoundTripScenario scenario)
    {
        var mistSource = GenerateInt64Program(scenario);
        var expected = scenario.A.ToString();

        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        if (exitCode != 0)
        {
            return false.ToProperty()
                .Label($"Mist program failed (exit code {exitCode}).\n" +
                       $"Scenario: {scenario}\n" +
                       $"Stderr:\n{stderr}\n" +
                       $"Source:\n{mistSource}");
        }

        var actual = stdout.Trim().Replace("\r\n", "\n");

        if (actual != expected)
        {
            return false.ToProperty()
                .Label($"Round-trip failed for {scenario}.\n" +
                       $"Expected: '{expected}'\n" +
                       $"Actual:   '{actual}'");
        }

        return true.ToProperty()
            .Label($"Verified: {scenario}");
    }
}
