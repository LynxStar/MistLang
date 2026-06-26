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

using System.Collections.Immutable;
using System.Diagnostics;
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
/// Batched property-based tests for Task 46 of the Mist Standard Library spec:
/// operator round-trip inverse.
///
/// Design § *Correctness Properties → Property 6*.
///
/// Each test generates 100 random scenarios, builds a single Mist program
/// that prints all 100 results (one per line), compiles once, runs once,
/// and compares each output line to the expected value.
///
/// **Validates: Requirements 12.7, 12.8**
/// </summary>
public sealed class OperatorRoundTripPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public OperatorRoundTripPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_rt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034
    public record Int32RoundTripScenario(int A, int B)
    {
        public override string ToString() => $"Int32: ({A} + {B}) - {B} == {A}";
    }

    public record Int64RoundTripScenario(long A, long B)
    {
        public override string ToString() => $"Int64: ({A} + {B}) - {B} == {A}";
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<Int32RoundTripScenario> Int32ScenarioGen =>
        from a in Gen.Choose(-10000, 10000)
        from b in Gen.Choose(-10000, 10000)
        select new Int32RoundTripScenario(a, b);

    private static Gen<Int64RoundTripScenario> Int64ScenarioGen =>
        from a in Gen.Choose(-10000, 10000)
        from b in Gen.Choose(-10000, 10000)
        select new Int64RoundTripScenario((long)a, (long)b);

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string FormatLiteral(long value) =>
        value < 0 ? $"(0 - {-value})" : value.ToString();

    /// <summary>
    /// Builds a single Mist program whose Main prints one result per
    /// scenario, one per line.
    /// </summary>
    private static string BuildBatchedInt32Program(IReadOnlyList<Int32RoundTripScenario> scenarios)
    {
        var body = new StringBuilder();
        for(var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            var a = FormatLiteral(s.A);
            var b = FormatLiteral(s.B);
            body.AppendLine($"        int a{i} = {a};");
            body.AppendLine($"        int b{i} = {b};");
            body.AppendLine($"        int s{i} = a{i} + b{i};");
            body.AppendLine($"        Console.WriteLine(s{i} - b{i});");
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

    private static string BuildBatchedInt64Program(IReadOnlyList<Int64RoundTripScenario> scenarios)
    {
        var body = new StringBuilder();
        for(var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            var a = FormatLiteral(s.A);
            var b = FormatLiteral(s.B);
            body.AppendLine($"        long a{i} = {a};");
            body.AppendLine($"        long b{i} = {b};");
            body.AppendLine($"        long s{i} = a{i} + b{i};");
            body.AppendLine($"        Console.WriteLine(s{i} - b{i});");
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

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Property 6 (Int32): (a + b) - b == a for 100 random Int32 operand pairs.
    /// Batched into a single compile + single run.
    ///
    /// **Validates: Requirements 12.7, 12.8**
    /// </summary>
    [Fact]
    public void Int32_AddSubtract_RoundTrip()
    {
        var scenarios = Int32ScenarioGen
            .Sample(IterationCount)
            .ToList();

        var mistSource = BuildBatchedInt32Program(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        Assert.Equal(scenarios.Count, lines.Length);

        for(var i = 0; i < scenarios.Count; i++)
        {
            var expected = scenarios[i].A.ToString();
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed: {scenarios[i]}\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }

    /// <summary>
    /// Property 6 (Int64): (a + b) - b == a for 100 random Int64 operand pairs.
    /// Batched into a single compile + single run.
    ///
    /// **Validates: Requirements 12.7, 12.8**
    /// </summary>
    [Fact]
    public void Int64_AddSubtract_RoundTrip()
    {
        var scenarios = Int64ScenarioGen
            .Sample(IterationCount)
            .ToList();

        var mistSource = BuildBatchedInt64Program(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        Assert.Equal(scenarios.Count, lines.Length);

        for(var i = 0; i < scenarios.Count; i++)
        {
            var expected = scenarios[i].A.ToString();
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed: {scenarios[i]}\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }
}