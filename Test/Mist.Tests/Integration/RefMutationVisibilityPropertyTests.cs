// Feature: ref-parameters, Property 2: Ref Mutation Visibility
// **Validates: Requirements 3.1, 3.2, 4.7, 5.4, 10.1, 10.2, 10.5**

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Batched property-based test for ref mutation visibility.
///
/// Feature: ref-parameters, Property 2: Ref Mutation Visibility
///
/// For any Mist program where a variable is passed as a <c>ref</c> argument
/// and the callee assigns a new value to the ref parameter, the caller's
/// variable SHALL hold the new value after the call returns. This holds for
/// int, double, and bool variables.
///
/// The test generates 100 random scenarios, builds a single Mist program
/// with one method per scenario that takes a ref parameter and assigns a new
/// value. Main() calls each method and prints the caller's variable after
/// the call. Compiles once, runs once, compares each output line.
///
/// **Validates: Requirements 3.1, 3.2, 4.7, 5.4, 10.1, 10.2, 10.5**
/// </summary>
public sealed class RefMutationVisibilityPropertyTests : IDisposable
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

    public RefMutationVisibilityPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_ref_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>
    /// Represents a single ref mutation scenario: a type, an initial value,
    /// and the value assigned by the callee.
    /// </summary>
#pragma warning disable CA1034, CA1720
    public enum RefType { Integer, Floating, Boolean }
#pragma warning restore CA1720

    public record RefMutationScenario(RefType Type, string InitialValue, string AssignedValue)
    {
        public override string ToString() =>
            $"{Type}: initial={InitialValue}, assigned={AssignedValue}";
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<RefMutationScenario> IntScenarioGen =>
        from initial in Gen.Choose(-1000, 1000)
        from assigned in Gen.Choose(-1000, 1000)
        where initial != assigned
        select new RefMutationScenario(
            RefType.Integer,
            FormatIntLiteral(initial),
            FormatIntLiteral(assigned));

    private static Gen<RefMutationScenario> DoubleScenarioGen =>
        from initial in Gen.Choose(-1000, 1000)
        from assigned in Gen.Choose(-1000, 1000)
        where initial != assigned
        let initialD = initial / 10.0
        let assignedD = assigned / 10.0
        select new RefMutationScenario(
            RefType.Floating,
            FormatDoubleLiteral(initialD),
            FormatDoubleLiteral(assignedD));

    private static Gen<RefMutationScenario> BoolScenarioGen =>
        from initial in Gen.Elements(true, false)
        from assigned in Gen.Elements(true, false)
        where initial != assigned
        select new RefMutationScenario(
            RefType.Boolean,
            initial.ToString().ToLowerInvariant(),
            assigned.ToString().ToLowerInvariant());

    private static Gen<RefMutationScenario> ScenarioGen =>
        Gen.OneOf(IntScenarioGen, DoubleScenarioGen, BoolScenarioGen);

    // ─── Literal formatting ──────────────────────────────────────────────

    private static string FormatIntLiteral(int value) =>
        value < 0 ? $"(0 - {-value})" : value.ToString(CultureInfo.InvariantCulture);

    private static string FormatDoubleLiteral(double value) =>
        value < 0
            ? $"(0.0 - {(-value).ToString("F1", CultureInfo.InvariantCulture)})"
            : value.ToString("F1", CultureInfo.InvariantCulture);

    private static string GetMistTypeName(RefType type) => type switch
    {
        RefType.Integer => "int",
        RefType.Floating => "double",
        RefType.Boolean => "bool",
        _ => "int"
    };

    // ─── Program builder ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a single Mist program with one mutator method per scenario.
    /// Each method takes a ref parameter and assigns the new value.
    /// Main() declares a variable, calls the method, and prints the result.
    /// </summary>
    private static string BuildBatchedProgram(IReadOnlyList<RefMutationScenario> scenarios)
    {
        var methods = new StringBuilder();
        var mainBody = new StringBuilder();

        for (var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            var typeName = GetMistTypeName(s.Type);

            // Generate the mutator method
            methods.AppendLine($"    private static void Mutate{i}(ref {typeName} v) {{");
            methods.AppendLine($"        v = {s.AssignedValue};");
            methods.AppendLine("    }");
            methods.AppendLine();

            // Generate the call in Main
            mainBody.AppendLine($"        {typeName} x{i} = {s.InitialValue};");
            mainBody.AppendLine($"        Mutate{i}(ref x{i});");
            mainBody.AppendLine($"        Console.WriteLine(x{i});");
        }

        return $$"""
            using Standard;

            namespace TestProgram;

            public class Program
            {
            {{methods}}
                public static void Main(string[] args)
                {
            {{mainBody}}    }
            }
            """;
    }

    // ─── Compile and run ─────────────────────────────────────────────────

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

    // ─── Expected output computation ─────────────────────────────────────

    private static string ComputeExpectedOutput(RefMutationScenario scenario)
    {
        return scenario.Type switch
        {
            RefType.Integer => int.Parse(
                scenario.AssignedValue.Replace("(0 - ", "-").Replace(")", ""),
                CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RefType.Floating => double.Parse(
                scenario.AssignedValue.Replace("(0.0 - ", "-").Replace(")", ""),
                CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RefType.Boolean => scenario.AssignedValue switch
            {
                "true" => "True",
                "false" => "False",
                _ => scenario.AssignedValue
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 2: Ref Mutation Visibility — For any Mist program where a
    /// variable is passed as ref and the callee assigns a new value, the
    /// caller's variable holds the new value after the call returns.
    ///
    /// 100 random scenarios batched into a single compile + single run.
    ///
    /// **Validates: Requirements 3.1, 3.2, 4.7, 5.4, 10.1, 10.2, 10.5**
    /// </summary>
    [Fact]
    public void RefMutationVisibility_CallerObservesCalleeAssignment()
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

        for (var i = 0; i < scenarios.Count; i++)
        {
            var expected = ComputeExpectedOutput(scenarios[i]);
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed: {scenarios[i]}\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }
}
