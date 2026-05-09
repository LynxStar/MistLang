// Feature: mist-regex-type, Property 5: End-to-end transpilation correctness (batched)
//
// **Validates: Requirements 8.1–8.10, 9.1, 9.2, 9.5**
//
// For any valid Mist program using regex features (regex literals, IsMatch calls,
// regex pattern matching in `is` expressions and switch arms), the C# transpiler
// SHALL produce output that:
// 1. Compiles without errors using the .NET C# compiler
// 2. Produces the expected runtime behavior (regex matching results)
//
// Uses the batched testing pattern:
// 1. Generate 100 random valid Mist programs using regex features
// 2. Build a single Mist program that prints 100 result lines (one per scenario)
// 3. Compile once in-process using CompilerDriver
// 4. Run the output DLL once (1 process spawn)
// 5. Compare results line-by-line
//
// Process spawns: 1 per test run (dotnet exec).

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Property-based test verifying end-to-end transpilation correctness for regex features.
///
/// Property 5: End-to-end transpilation correctness (batched)
/// For any valid Mist program using regex features (regex literals, IsMatch calls,
/// regex pattern matching in `is` expressions and switch arms), the C# transpiler
/// SHALL produce output that compiles without errors and produces the expected runtime behavior.
///
/// Uses the batched testing pattern:
/// 1. Generate 100 random valid Mist programs using regex features
/// 2. Build a single Mist program that prints 100 result lines (one per scenario)
/// 3. Compile once in-process using CompilerDriver
/// 4. Run the output DLL once (1 process spawn)
/// 5. Compare results line-by-line
///
/// Process spawns: 1 per test run (dotnet exec).
///
/// **Validates: Requirements 8.1–8.10, 9.1, 9.2, 9.5**
/// </summary>
public sealed class RegexTranspilationPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public RegexTranspilationPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "mist_regex_e2e_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario model ──────────────────────────────────────────────────

    private enum ScenarioKind
    {
        IsMatch,
        IsExpression,
        SwitchExpression,
    }

    private sealed class RegexScenario
    {
        public required ScenarioKind Kind { get; init; }
        public required string Pattern { get; init; }
        public required string Input { get; init; }
        public required string ExpectedOutput { get; init; }
    }

    // ─── Valid regex patterns (safe for embedding in backtick literals) ───

    private static readonly string[] SafePatterns =
    {
        "[a-z]+",
        "[A-Z]+",
        "[0-9]+",
        "[a-zA-Z]+",
        "[a-zA-Z0-9]+",
        ".*",
        "hello",
        "world",
        "test",
        "foo",
        "bar",
        "[abc]+",
        "[xyz]+",
        "[0-9]{2,4}",
        "[a-z]{1,5}",
        "(foo|bar)",
        "(hello|world)",
        "(a|b|c)+",
        "[^x]+",
        "a+",
    };

    /// <summary>
    /// Test inputs that are simple ASCII strings safe for embedding in Mist string literals.
    /// </summary>
    private static readonly string[] SafeInputs =
    {
        "hello",
        "world",
        "test",
        "foo",
        "bar",
        "abc",
        "xyz",
        "123",
        "4567",
        "Hello",
        "WORLD",
        "TEST",
        "foobar",
        "abcxyz",
        "a",
        "b",
        "c",
        "ab",
        "aaa",
        "zzz",
    };

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<RegexScenario> GenIsMatchScenario()
    {
        return from patternIdx in Gen.Choose(0, SafePatterns.Length - 1)
               from inputIdx in Gen.Choose(0, SafeInputs.Length - 1)
               let pattern = SafePatterns[patternIdx]
               let input = SafeInputs[inputIdx]
               let expected = Regex.IsMatch(input, pattern).ToString()
               select new RegexScenario
               {
                   Kind = ScenarioKind.IsMatch,
                   Pattern = pattern,
                   Input = input,
                   ExpectedOutput = expected,
               };
    }

    private static Gen<RegexScenario> GenIsExpressionScenario()
    {
        return from patternIdx in Gen.Choose(0, SafePatterns.Length - 1)
               from inputIdx in Gen.Choose(0, SafeInputs.Length - 1)
               let pattern = SafePatterns[patternIdx]
               let input = SafeInputs[inputIdx]
               // is expression uses full-match semantics (anchored with ^(?:...)$)
               let expected = Regex.IsMatch(input, "^(?:" + pattern + ")$").ToString()
               select new RegexScenario
               {
                   Kind = ScenarioKind.IsExpression,
                   Pattern = pattern,
                   Input = input,
                   ExpectedOutput = expected,
               };
    }

    private static Gen<RegexScenario> GenSwitchExpressionScenario()
    {
        return from inputIdx in Gen.Choose(0, SafeInputs.Length - 1)
               let input = SafeInputs[inputIdx]
               // Switch uses full-match semantics: test against three fixed patterns
               let matchesDigits = Regex.IsMatch(input, "^(?:[0-9]+)$")
               let matchesLower = Regex.IsMatch(input, "^(?:[a-z]+)$")
               let matchesUpper = Regex.IsMatch(input, "^(?:[A-Z]+)$")
               let expected = matchesDigits ? "digits"
                            : matchesLower ? "lower"
                            : matchesUpper ? "upper"
                            : "other"
               select new RegexScenario
               {
                   Kind = ScenarioKind.SwitchExpression,
                   Pattern = "", // not used directly; switch uses fixed patterns
                   Input = input,
                   ExpectedOutput = expected,
               };
    }

    private static Gen<RegexScenario> GenScenario()
    {
        return Gen.OneOf(
            GenIsMatchScenario(),
            GenIsExpressionScenario(),
            GenSwitchExpressionScenario());
    }

    // ─── Batched program builder ─────────────────────────────────────────

    /// <summary>
    /// Builds a single Mist program whose Main evaluates all scenarios
    /// and prints one result per line.
    /// </summary>
    private static string BuildBatchedProgram(IReadOnlyList<RegexScenario> scenarios)
    {
        var body = new StringBuilder();

        for (var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            switch (s.Kind)
            {
                case ScenarioKind.IsMatch:
                    body.AppendLine($"        var r{i} = `{EscapeForMistRegex(s.Pattern)}`;");
                    body.AppendLine($"        Console.WriteLine(r{i}.IsMatch(\"{EscapeForMistString(s.Input)}\"));");
                    break;

                case ScenarioKind.IsExpression:
                    body.AppendLine($"        string s{i} = \"{EscapeForMistString(s.Input)}\";");
                    body.AppendLine($"        bool b{i} = s{i} is `{EscapeForMistRegex(s.Pattern)}`;");
                    body.AppendLine($"        Console.WriteLine(b{i});");
                    break;

                case ScenarioKind.SwitchExpression:
                    body.AppendLine($"        string sw{i} = \"{EscapeForMistString(s.Input)}\";");
                    body.AppendLine($"        var cat{i} = sw{i} switch {{");
                    body.AppendLine($"            `[0-9]+` => \"digits\",");
                    body.AppendLine($"            `[a-z]+` => \"lower\",");
                    body.AppendLine($"            `[A-Z]+` => \"upper\",");
                    body.AppendLine($"            var _ => \"other\",");
                    body.AppendLine($"        }};");
                    body.AppendLine($"        Console.WriteLine(cat{i});");
                    break;
            }
        }

        return $$"""
            using System;

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
    /// Escapes a pattern for embedding inside backtick-delimited regex literals in Mist.
    /// Backticks become \` and backslashes become \\.
    /// </summary>
    private static string EscapeForMistRegex(string pattern)
    {
        return pattern.Replace("\\", "\\\\").Replace("`", "\\`");
    }

    /// <summary>
    /// Escapes a string for embedding inside double-quoted string literals in Mist.
    /// </summary>
    private static string EscapeForMistString(string input)
    {
        return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
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

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 5: End-to-end transpilation correctness (batched).
    ///
    /// Batched pattern: generate 100 random valid Mist programs using regex features,
    /// build a single program that prints 100 result lines, compile once in-process
    /// via CompilerDriver, run the output DLL once, compare results line-by-line.
    ///
    /// Process spawns: 1 (dotnet exec).
    ///
    /// **Validates: Requirements 8.1–8.10, 9.1, 9.2, 9.5**
    /// </summary>
    [Fact]
    public void RegexFeatures_ProduceCorrectRuntimeBehavior_Batched()
    {
        // Step 1: Generate 100 random scenarios using FsCheck
        var scenarios = GenScenario().Sample(IterationCount).ToList();

        // Step 2: Build a single batched Mist program
        var mistSource = BuildBatchedProgram(scenarios);

        // Step 3 & 4: Compile in-process and run the output DLL once
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}\n\n" +
            $"Source (first 3000 chars):\n{mistSource[..Math.Min(3000, mistSource.Length)]}");

        // Step 5: Compare results line-by-line
        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        Assert.True(lines.Length == scenarios.Count,
            $"Expected {scenarios.Count} output lines but got {lines.Length}.\n" +
            $"Stdout:\n{stdout}");

        for (var i = 0; i < scenarios.Count; i++)
        {
            var expected = scenarios[i].ExpectedOutput;
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed ({scenarios[i].Kind}, pattern='{scenarios[i].Pattern}', " +
                $"input='{scenarios[i].Input}'):\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }
}
