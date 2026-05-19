// Feature: type-unions, Property 14: Emitted C# Semantic Equivalence
// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 11.1, 11.2, 11.3**

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based test verifying emitted C# semantic equivalence for union types.
///
/// Property 14: For any Mist program containing union-typed values and pattern matches
/// on those values, the emitted C# code SHALL compile without errors and produce the
/// same observable behavior (console output, return values) as specified by Mist's
/// type union semantics.
///
/// Uses the batched testing pattern:
/// 1. Generate N scenarios (different union type usage patterns)
/// 2. Build a single Mist program exercising all N scenarios
/// 3. Compile once (in-process via CompilerDriver)
/// 4. Run once (1 process spawn via dotnet exec)
/// 5. Compare N output lines against expected values
///
/// Process spawns: ≤1 per test run.
/// </summary>
public sealed class UnionSemanticEquivalencePropertyTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public UnionSemanticEquivalencePropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_union_semantic_prop_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Each scenario exercises a specific union type usage pattern and prints
    /// a deterministic expected output line.
    /// </summary>
    private sealed class Scenario
    {
        public required string Description { get; init; }
        public required string MethodBody { get; init; }
        public required string ExpectedOutput { get; init; }
    }

    /// <summary>
    /// Generates the full set of scenarios covering:
    /// - Assign int literal to int | string → verify via pattern match (Req 10.1, 10.5, 10.6, 11.1)
    /// - Assign string literal to int | string → verify via pattern match (Req 10.1, 10.5, 10.6, 11.1)
    /// - Pass constituent to union-typed parameter (Req 10.2, 10.5)
    /// - Return union from method → verify correct constituent (Req 10.3, 10.5, 10.6, 11.1)
    /// - Pattern match with is var v → verify binding works (Req 10.6, 11.2, 11.3)
    /// - Field with union type (Req 10.4, 10.5)
    /// - Multiple pattern match branches (Req 10.6, 11.1)
    /// - Nested method calls with union return (Req 10.3, 10.5)
    /// - Pattern match on string constituent extracts value (Req 11.1, 11.2)
    /// - Pattern match on int constituent extracts value (Req 11.1, 11.2)
    ///
    /// We generate 25 scenarios to thoroughly exercise the feature.
    /// </summary>
    private static List<Scenario> GenerateScenarios()
    {
        var scenarios = new List<Scenario>();

        // Scenario 1: Assign int literal to int | string, verify it's int via pattern match
        scenarios.Add(new Scenario
        {
            Description = "assign_int_verify_pattern",
            MethodBody = """
                        int | string x = 42;
                        if (x is int n) {
                            Console.WriteLine("scenario_1_ok");
                        } else {
                            Console.WriteLine("scenario_1_fail");
                        }
            """,
            ExpectedOutput = "scenario_1_ok",
        });

        // Scenario 2: Assign string literal to int | string, verify it's string via pattern match
        scenarios.Add(new Scenario
        {
            Description = "assign_string_verify_pattern",
            MethodBody = """
                        int | string y = "hello";
                        if (y is string s) {
                            Console.WriteLine("scenario_2_ok");
                        } else {
                            Console.WriteLine("scenario_2_fail");
                        }
            """,
            ExpectedOutput = "scenario_2_ok",
        });

        // Scenario 3: Assign int, verify it's NOT string
        scenarios.Add(new Scenario
        {
            Description = "assign_int_not_string",
            MethodBody = """
                        int | string z = 100;
                        if (z is string s2) {
                            Console.WriteLine("scenario_3_fail");
                        } else {
                            Console.WriteLine("scenario_3_ok");
                        }
            """,
            ExpectedOutput = "scenario_3_ok",
        });

        // Scenario 4: Assign string, verify it's NOT int
        scenarios.Add(new Scenario
        {
            Description = "assign_string_not_int",
            MethodBody = """
                        int | string w = "world";
                        if (w is int n2) {
                            Console.WriteLine("scenario_4_fail");
                        } else {
                            Console.WriteLine("scenario_4_ok");
                        }
            """,
            ExpectedOutput = "scenario_4_ok",
        });

        // Scenario 5: Pass int constituent to union-typed parameter
        scenarios.Add(new Scenario
        {
            Description = "pass_int_to_union_param",
            MethodBody = """
                        Program.AcceptUnion(55);
            """,
            ExpectedOutput = "scenario_5_ok",
        });

        // Scenario 6: Pass string constituent to union-typed parameter
        scenarios.Add(new Scenario
        {
            Description = "pass_string_to_union_param",
            MethodBody = """
                        Program.AcceptString("test");
            """,
            ExpectedOutput = "scenario_6_ok",
        });

        // Scenario 7: Return int from union-returning method, verify via pattern match
        scenarios.Add(new Scenario
        {
            Description = "return_int_from_method",
            MethodBody = """
                        int | string r1 = Program.GetUnionValue(true);
                        if (r1 is int ri) {
                            Console.WriteLine("scenario_7_ok");
                        } else {
                            Console.WriteLine("scenario_7_fail");
                        }
            """,
            ExpectedOutput = "scenario_7_ok",
        });

        // Scenario 8: Return string from union-returning method, verify via pattern match
        scenarios.Add(new Scenario
        {
            Description = "return_string_from_method",
            MethodBody = """
                        int | string r2 = Program.GetUnionValue(false);
                        if (r2 is string rs) {
                            Console.WriteLine("scenario_8_ok");
                        } else {
                            Console.WriteLine("scenario_8_fail");
                        }
            """,
            ExpectedOutput = "scenario_8_ok",
        });

        // Scenario 9: Pattern match with is var v on int value
        scenarios.Add(new Scenario
        {
            Description = "var_pattern_int",
            MethodBody = """
                        int | string vi = 77;
                        if (vi is var bound1) {
                            Console.WriteLine("scenario_9_ok");
                        }
            """,
            ExpectedOutput = "scenario_9_ok",
        });

        // Scenario 10: Pattern match with is var v on string value
        scenarios.Add(new Scenario
        {
            Description = "var_pattern_string",
            MethodBody = """
                        int | string vs = "bound";
                        if (vs is var bound2) {
                            Console.WriteLine("scenario_10_ok");
                        }
            """,
            ExpectedOutput = "scenario_10_ok",
        });

        // Scenario 11: Field with union type, assign int
        scenarios.Add(new Scenario
        {
            Description = "field_assign_int",
            MethodBody = """
                        Holder h1 = new Holder();
                        h1.data = 123;
                        if (h1.data is int fi) {
                            Console.WriteLine("scenario_11_ok");
                        } else {
                            Console.WriteLine("scenario_11_fail");
                        }
            """,
            ExpectedOutput = "scenario_11_ok",
        });

        // Scenario 12: Field with union type, assign string
        scenarios.Add(new Scenario
        {
            Description = "field_assign_string",
            MethodBody = """
                        Holder h2 = new Holder();
                        h2.data = "stored";
                        if (h2.data is string fs) {
                            Console.WriteLine("scenario_12_ok");
                        } else {
                            Console.WriteLine("scenario_12_fail");
                        }
            """,
            ExpectedOutput = "scenario_12_ok",
        });

        // Scenario 13: Reassign union variable from int to string
        scenarios.Add(new Scenario
        {
            Description = "reassign_int_to_string",
            MethodBody = """
                        int | string mut = 1;
                        mut = "changed";
                        if (mut is string ms) {
                            Console.WriteLine("scenario_13_ok");
                        } else {
                            Console.WriteLine("scenario_13_fail");
                        }
            """,
            ExpectedOutput = "scenario_13_ok",
        });

        // Scenario 14: Reassign union variable from string to int
        scenarios.Add(new Scenario
        {
            Description = "reassign_string_to_int",
            MethodBody = """
                        int | string mut2 = "first";
                        mut2 = 999;
                        if (mut2 is int mi) {
                            Console.WriteLine("scenario_14_ok");
                        } else {
                            Console.WriteLine("scenario_14_fail");
                        }
            """,
            ExpectedOutput = "scenario_14_ok",
        });

        // Scenario 15: Pattern match extracts correct int value
        scenarios.Add(new Scenario
        {
            Description = "extract_int_value",
            MethodBody = """
                        int | string ev = 42;
                        if (ev is int extracted) {
                            if (extracted == 42) {
                                Console.WriteLine("scenario_15_ok");
                            } else {
                                Console.WriteLine("scenario_15_fail_value");
                            }
                        } else {
                            Console.WriteLine("scenario_15_fail_type");
                        }
            """,
            ExpectedOutput = "scenario_15_ok",
        });

        // Scenario 16: Pattern match extracts correct string value
        scenarios.Add(new Scenario
        {
            Description = "extract_string_value",
            MethodBody = """
                        int | string sv = "extract_me";
                        if (sv is string extracted2) {
                            if (extracted2 == "extract_me") {
                                Console.WriteLine("scenario_16_ok");
                            } else {
                                Console.WriteLine("scenario_16_fail_value");
                            }
                        } else {
                            Console.WriteLine("scenario_16_fail_type");
                        }
            """,
            ExpectedOutput = "scenario_16_ok",
        });

        // Scenario 17: Union with bool | string, assign bool
        scenarios.Add(new Scenario
        {
            Description = "bool_string_assign_bool",
            MethodBody = """
                        bool | string bs = true;
                        if (bs is bool bv) {
                            Console.WriteLine("scenario_17_ok");
                        } else {
                            Console.WriteLine("scenario_17_fail");
                        }
            """,
            ExpectedOutput = "scenario_17_ok",
        });

        // Scenario 18: Union with bool | string, assign string
        scenarios.Add(new Scenario
        {
            Description = "bool_string_assign_string",
            MethodBody = """
                        bool | string bs2 = "flag";
                        if (bs2 is string bsv) {
                            Console.WriteLine("scenario_18_ok");
                        } else {
                            Console.WriteLine("scenario_18_fail");
                        }
            """,
            ExpectedOutput = "scenario_18_ok",
        });

        // Scenario 19: Three-type union int | string | bool, assign int
        scenarios.Add(new Scenario
        {
            Description = "three_type_union_int",
            MethodBody = """
                        int | string | bool tri = 7;
                        if (tri is int tv) {
                            Console.WriteLine("scenario_19_ok");
                        } else {
                            Console.WriteLine("scenario_19_fail");
                        }
            """,
            ExpectedOutput = "scenario_19_ok",
        });

        // Scenario 20: Three-type union int | string | bool, assign string
        scenarios.Add(new Scenario
        {
            Description = "three_type_union_string",
            MethodBody = """
                        int | string | bool tri2 = "three";
                        if (tri2 is string tsv) {
                            Console.WriteLine("scenario_20_ok");
                        } else {
                            Console.WriteLine("scenario_20_fail");
                        }
            """,
            ExpectedOutput = "scenario_20_ok",
        });

        // Scenario 21: Three-type union int | string | bool, assign bool
        scenarios.Add(new Scenario
        {
            Description = "three_type_union_bool",
            MethodBody = """
                        int | string | bool tri3 = false;
                        if (tri3 is bool tbv) {
                            Console.WriteLine("scenario_21_ok");
                        } else {
                            Console.WriteLine("scenario_21_fail");
                        }
            """,
            ExpectedOutput = "scenario_21_ok",
        });

        // Scenario 22: Pass union variable to union-typed parameter
        scenarios.Add(new Scenario
        {
            Description = "pass_union_var_to_param",
            MethodBody = """
                        int | string pv = 88;
                        Program.AcceptAndCheck(pv);
            """,
            ExpectedOutput = "scenario_22_ok",
        });

        // Scenario 23: Return from method and immediately pattern match
        scenarios.Add(new Scenario
        {
            Description = "return_and_match_inline",
            MethodBody = """
                        if (Program.GetUnionValue(true) is int iv) {
                            Console.WriteLine("scenario_23_ok");
                        } else {
                            Console.WriteLine("scenario_23_fail");
                        }
            """,
            ExpectedOutput = "scenario_23_ok",
        });

        // Scenario 24: var pattern preserves value identity (int)
        scenarios.Add(new Scenario
        {
            Description = "var_preserves_int",
            MethodBody = """
                        int | string vp = 55;
                        if (vp is var vbound) {
                            if (vbound is int vpn) {
                                Console.WriteLine("scenario_24_ok");
                            } else {
                                Console.WriteLine("scenario_24_fail_inner");
                            }
                        }
            """,
            ExpectedOutput = "scenario_24_ok",
        });

        // Scenario 25: var pattern preserves value identity (string)
        scenarios.Add(new Scenario
        {
            Description = "var_preserves_string",
            MethodBody = """
                        int | string vp2 = "kept";
                        if (vp2 is var vbound2) {
                            if (vbound2 is string vps) {
                                Console.WriteLine("scenario_25_ok");
                            } else {
                                Console.WriteLine("scenario_25_fail_inner");
                            }
                        }
            """,
            ExpectedOutput = "scenario_25_ok",
        });

        return scenarios;
    }

    // ─── Mist program builder ────────────────────────────────────────────

    /// <summary>
    /// Builds a single Mist program that exercises all scenarios, printing one
    /// output line per scenario.
    /// </summary>
    private static string BuildBatchedProgram(List<Scenario> scenarios)
    {
        var scenarioMethods = new System.Text.StringBuilder();

        for (var i = 0; i < scenarios.Count; i++)
        {
            scenarioMethods.AppendLine($"    public static void RunScenario{i + 1}() {{");
            scenarioMethods.AppendLine(scenarios[i].MethodBody);
            scenarioMethods.AppendLine("    }");
            scenarioMethods.AppendLine();
        }

        var mainCalls = new System.Text.StringBuilder();
        for (var i = 0; i < scenarios.Count; i++)
        {
            mainCalls.AppendLine($"        Program.RunScenario{i + 1}();");
        }

        return $$"""
            using Standard;

            namespace TestProgram;

            public class Holder {
                public int | string data;
            }

            public class Program {
                public static int | string GetUnionValue(bool flag) {
                    if (flag) {
                        return 42;
                    }
                    return "hello";
                }

                public static void AcceptUnion(int | string value) {
                    if (value is int n) {
                        Console.WriteLine("scenario_5_ok");
                    } else {
                        Console.WriteLine("scenario_5_fail");
                    }
                }

                public static void AcceptString(int | string value) {
                    if (value is string s) {
                        Console.WriteLine("scenario_6_ok");
                    } else {
                        Console.WriteLine("scenario_6_fail");
                    }
                }

                public static void AcceptAndCheck(int | string value) {
                    if (value is int n) {
                        Console.WriteLine("scenario_22_ok");
                    } else {
                        Console.WriteLine("scenario_22_fail");
                    }
                }

            {{scenarioMethods}}
                public static void Main(string[] args) {
            {{mainCalls}}    }
            }
            """;
    }

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
    /// Property 14: Emitted C# Semantic Equivalence
    ///
    /// Batched pattern: 25 scenarios, 1 compile, 1 process spawn, 25 output lines compared.
    ///
    /// Scenarios cover:
    /// - Assign int/string literal to int | string → verify via pattern match (Req 10.1, 10.5, 10.6, 11.1)
    /// - Pass constituent to union-typed parameter (Req 10.2, 10.5)
    /// - Return union from method → verify correct constituent (Req 10.3, 10.5, 10.6, 11.1)
    /// - Pattern match with is var v → verify binding works (Req 10.6, 11.2, 11.3)
    /// - Field with union type (Req 10.4, 10.5)
    /// - Three-type unions (Req 10.1, 10.5, 10.6, 11.1)
    /// - Value extraction via pattern match (Req 11.1, 11.2)
    ///
    /// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 11.1, 11.2, 11.3**
    /// </summary>
    [Fact]
    public void EmittedCSharp_SemanticEquivalence_BatchedScenarios()
    {
        var scenarios = GenerateScenarios();
        var mistSource = BuildBatchedProgram(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}\n\nSource:\n{mistSource}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        Assert.True(lines.Length == scenarios.Count,
            $"Expected {scenarios.Count} output lines, got {lines.Length}.\nStdout:\n{stdout}");

        for (var i = 0; i < scenarios.Count; i++)
        {
            Assert.True(lines[i] == scenarios[i].ExpectedOutput,
                $"Scenario {i + 1} ({scenarios[i].Description}) failed: " +
                $"expected '{scenarios[i].ExpectedOutput}', got '{lines[i]}'");
        }
    }
}
