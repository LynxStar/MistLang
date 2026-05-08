// Feature: switch-expressions, Property 9: End-to-End Emission Correctness
// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.9, 10.10, 10.11, 10.12**

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for end-to-end switch expression emission correctness.
///
/// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.9, 10.10, 10.11, 10.12**
///
/// For any valid, exhaustive Mist switch expression, the C# emitter SHALL produce output
/// that compiles as valid C# and produces the correct runtime result.
///
/// Batched: generate all scenarios upfront, compile once, run once, compare output lines.
/// </summary>
public sealed class SwitchEmissionPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver ───────────────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public SwitchEmissionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_switch_pbt_" + Guid.NewGuid().ToString("N"));
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

#pragma warning disable CA1034
    public record SwitchScenario(string MistBody, string ExpectedOutput, string Label)
    {
        public override string ToString() => Label;
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<SwitchScenario> ScenarioGen()
    {
        var intConstantSwitch = from value in Gen.Choose(0, 4)
                                let expected = value switch
                                {
                                    0 => "zero",
                                    1 => "one",
                                    2 => "two",
                                    _ => "other",
                                }
                                let body = $"int x = {value};\n        string r = x switch {{ 0 => \"zero\", 1 => \"one\", 2 => \"two\", var _ => \"other\" }};\n        Console.WriteLine(r);"
                                select new SwitchScenario(body, expected, $"int constant switch({value})");

        var boolSwitch = from value in Gen.Elements(true, false)
                         let expected = value ? "yes" : "no"
                         let boolLit = value ? "true" : "false"
                         let body = $"bool b = {boolLit};\n        string r = b switch {{ true => \"yes\", false => \"no\" }};\n        Console.WriteLine(r);"
                         select new SwitchScenario(body, expected, $"bool switch({boolLit})");

        var stringConstantSwitch = from idx in Gen.Choose(0, 2)
                                   let values = new[] { "hello", "world", "foo" }
                                   let value = values[idx]
                                   let expected = value == "hello" ? "greeting" : value == "world" ? "place" : "other"
                                   let body = $"string s = \"{value}\";\n        string r = s switch {{ \"hello\" => \"greeting\", \"world\" => \"place\", var _ => \"other\" }};\n        Console.WriteLine(r);"
                                   select new SwitchScenario(body, expected, $"string switch(\"{value}\")");

        var guardedSwitch = from value in Gen.Choose(-5, 10)
                            let expected = value > 0 ? "positive" : "non-positive"
                            let valueLit = value < 0 ? $"(0 - {-value})" : value.ToString()
                            let body = $"int x = {valueLit};\n        string r = x switch {{ int n when n > 0 => \"positive\", var _ => \"non-positive\" }};\n        Console.WriteLine(r);"
                            select new SwitchScenario(body, expected, $"guarded switch({value})");

        var relationalSwitch = from value in Gen.Choose(0, 100)
                               let expected = value switch
                               {
                                   <= 25 => "low",
                                   <= 50 => "medium",
                                   <= 75 => "high",
                                   _ => "very-high",
                               }
                               let body = $"int x = {value};\n        string r = x switch {{ <= 25 => \"low\", <= 50 => \"medium\", <= 75 => \"high\", var _ => \"very-high\" }};\n        Console.WriteLine(r);"
                               select new SwitchScenario(body, expected, $"relational switch({value})");

        var varPatternSwitch = from value in Gen.Choose(0, 50)
                               let expected = "caught"
                               let body = $"int x = {value};\n        string r = x switch {{ var n => \"caught\" }};\n        Console.WriteLine(r);"
                               select new SwitchScenario(body, expected, $"var pattern switch({value})");

        return Gen.OneOf(
            intConstantSwitch,
            boolSwitch,
            stringConstantSwitch,
            guardedSwitch,
            relationalSwitch,
            varPatternSwitch);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string BuildBatchedProgram(IReadOnlyList<SwitchScenario> scenarios)
    {
        var body = new StringBuilder();
        for (var i = 0; i < scenarios.Count; i++)
        {
            // Replace variable names with unique ones per scenario to avoid duplicates
            var scenarioBody = scenarios[i].MistBody
                .Replace("int x ", $"int x{i} ")
                .Replace("bool b ", $"bool b{i} ")
                .Replace("string s ", $"string s{i} ")
                .Replace("string r ", $"string r{i} ")
                .Replace("x switch", $"x{i} switch")
                .Replace("b switch", $"b{i} switch")
                .Replace("s switch", $"s{i} switch")
                .Replace("Console.WriteLine(r)", $"Console.WriteLine(r{i})");
            body.Append($"        {scenarioBody}\n");
        }

        return "using System;\n\nnamespace TestProgram;\n\npublic class Program\n{\n    public static void Main()\n    {\n"
            + body
            + "    }\n}\n";
    }

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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: End-to-End Emission Correctness
    //
    // **Validates: Requirements 10.1–10.12**
    //
    // For any valid, exhaustive Mist switch expression, the C# emitter
    // produces output that compiles as valid C# and produces the correct
    // runtime result. Batched: 1 compile + 1 run for all scenarios.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SwitchExpressions_EmitCorrectCSharp_AndProduceCorrectOutput()
    {
        var scenarios = ScenarioGen()
            .Sample(IterationCount)
            .ToList();

        var mistSource = BuildBatchedProgram(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}\nSource:\n{mistSource}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        Assert.Equal(scenarios.Count, lines.Length);

        for (var i = 0; i < scenarios.Count; i++)
        {
            var expected = scenarios[i].ExpectedOutput;
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed: {scenarios[i].Label}\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }
}
