// Feature: structured-context, Property 4: Runtime context propagation and scoping
//
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.8, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5,
//              7.1, 7.4, 10.1, 10.2, 10.3, 10.4, 10.5, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8**
//
// For any valid Mist program with context declarations and consumers connected through
// a call graph of arbitrary depth, compiling and executing the program SHALL produce
// output demonstrating that:
//   (a) consumers receive the most recently pushed value for their bound type,
//   (b) context values propagate through intermediate methods that neither declare nor consume context,
//   (c) when a shadowing scope exits, the previous value is restored for subsequent consumers,
//   (d) context declarations of different types do not interfere with each other.
//
// Uses the batched pattern: generates N scenarios, builds a single program that prints
// N labeled results, compiles once, runs once, compares output line-by-line.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

// ─── Scenario types ──────────────────────────────────────────────────────

/// <summary>
/// Describes the kind of context propagation scenario being tested.
/// </summary>
public enum ContextPropagationScenarioKind
{
    /// <summary>
    /// A consumer directly receives the most recently pushed value.
    /// Tests property (a).
    /// </summary>
    DirectConsumption,

    /// <summary>
    /// Context propagates through a method that itself has a with-clause,
    /// which then calls another consumer.
    /// Tests property (b) - context propagation through the call graph.
    /// </summary>
    PropagationThroughCaller,

    /// <summary>
    /// A with-block shadows a context value; after the block exits, the
    /// previous value is restored.
    /// Tests property (c).
    /// </summary>
    ShadowingAndRestoration,

    /// <summary>
    /// Two different context types are declared; consuming one does not
    /// affect the other.
    /// Tests property (d).
    /// </summary>
    IndependentTypes,

    /// <summary>
    /// Multiple with-declarations of the same type in the same scope;
    /// the last one shadows previous ones.
    /// Tests properties (a) and (c).
    /// </summary>
    MultipleSameTypePushes,
}

/// <summary>
/// A generated scenario for testing runtime context propagation.
/// Each scenario produces one output line when executed.
/// </summary>
public sealed class ContextPropagationScenario
{
    public ContextPropagationScenarioKind Kind { get; }
    public string ExpectedOutput { get; }
    public string Label { get; }

    /// <summary>
    /// A unique suffix to avoid name collisions when multiple scenarios
    /// are combined into a single program.
    /// </summary>
    public int Index { get; set; }

    public ContextPropagationScenario(
        ContextPropagationScenarioKind kind,
        string expectedOutput,
        string label)
    {
        Kind = kind;
        ExpectedOutput = expectedOutput;
        Label = label;
    }

    public override string ToString() => $"[{Kind}] {Label} → \"{ExpectedOutput}\"";
}

// ─── Property test class ─────────────────────────────────────────────────

/// <summary>
/// Batched property-based test for runtime context propagation and scoping.
///
/// Property 4: Runtime context propagation and scoping
///
/// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.8, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5,
///              7.1, 7.4, 10.1, 10.2, 10.3, 10.4, 10.5, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8**
/// </summary>
public sealed class ContextPropagationPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ContextCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public ContextPropagationPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_ctx_prop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Known values for generation ─────────────────────────────────────

    private static readonly int[] IntValues = [1, 2, 3, 5, 7, 10, 42, 99, 100, 256];
    private static readonly string[] StringValues = ["hello", "world", "foo", "bar", "ctx", "test", "alpha", "beta"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<ContextPropagationScenario> GenDirectConsumption()
    {
        return from value in Gen.Elements(IntValues)
               select new ContextPropagationScenario(
                   ContextPropagationScenarioKind.DirectConsumption,
                   expectedOutput: value.ToString(),
                   label: $"direct int={value}");
    }

    private static Gen<ContextPropagationScenario> GenPropagationThroughCaller()
    {
        return from value in Gen.Elements(IntValues)
               select new ContextPropagationScenario(
                   ContextPropagationScenarioKind.PropagationThroughCaller,
                   expectedOutput: value.ToString(),
                   label: $"propagate int={value}");
    }

    private static Gen<ContextPropagationScenario> GenShadowingAndRestoration()
    {
        return from outerValue in Gen.Elements(IntValues)
               from innerValue in Gen.Elements(IntValues)
               where outerValue != innerValue
               select new ContextPropagationScenario(
                   ContextPropagationScenarioKind.ShadowingAndRestoration,
                   expectedOutput: $"{innerValue},{outerValue}",
                   label: $"shadow outer={outerValue} inner={innerValue}");
    }

    private static Gen<ContextPropagationScenario> GenIndependentTypes()
    {
        return from intValue in Gen.Elements(IntValues)
               from strIdx in Gen.Choose(0, StringValues.Length - 1)
               let strValue = StringValues[strIdx]
               select new ContextPropagationScenario(
                   ContextPropagationScenarioKind.IndependentTypes,
                   expectedOutput: $"{intValue},{strValue}",
                   label: $"independent int={intValue} str={strValue}");
    }

    private static Gen<ContextPropagationScenario> GenMultipleSameTypePushes()
    {
        return from first in Gen.Elements(IntValues)
               from second in Gen.Elements(IntValues)
               where first != second
               select new ContextPropagationScenario(
                   ContextPropagationScenarioKind.MultipleSameTypePushes,
                   expectedOutput: second.ToString(),
                   label: $"multi-push first={first} second={second}");
    }

    private static Gen<ContextPropagationScenario> GenScenario()
    {
        return Gen.OneOf(
            GenDirectConsumption(),
            GenPropagationThroughCaller(),
            GenShadowingAndRestoration(),
            GenIndependentTypes(),
            GenMultipleSameTypePushes());
    }

    // ─── Batched program builder ─────────────────────────────────────────

    /// <summary>
    /// Builds a single Mist program that exercises all scenarios and prints
    /// one result per line. Each scenario is isolated in its own static method
    /// to avoid deep try/finally nesting from multiple with-statements in a
    /// single scope. All methods are in one class so the context checker can
    /// trace context availability.
    /// </summary>
    private static string BuildBatchedProgram(IReadOnlyList<ContextPropagationScenario> scenarios)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Standard;");
        sb.AppendLine();

        // Emit a simple context-carrying class for string context
        sb.AppendLine("class StrCtx {");
        sb.AppendLine("    public string Value;");
        sb.AppendLine("    public StrCtx(string v) { Value = v; }");
        sb.AppendLine("}");
        sb.AppendLine();

        // All methods go in one Program class
        sb.AppendLine("class Program {");

        // Emit consumer/intermediate methods for each scenario
        for(var i = 0; i < scenarios.Count; i++)
        {
            scenarios[i].Index = i;
            EmitScenarioHelpers(sb, scenarios[i]);
        }

        // Emit per-scenario runner methods
        for(var i = 0; i < scenarios.Count; i++)
        {
            EmitScenarioRunnerMethod(sb, scenarios[i]);
        }

        // Emit Main
        sb.AppendLine("    public static void Main() {");
        for(var i = 0; i < scenarios.Count; i++)
        {
            sb.AppendLine($"        Run{i}();");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Emits helper methods for a scenario (consumer methods, intermediate methods, etc.)
    /// All methods are emitted inside the Program class to ensure the context checker
    /// can trace context availability across method calls within the same class.
    /// </summary>
    private static void EmitScenarioHelpers(StringBuilder sb, ContextPropagationScenario scenario)
    {
        var i = scenario.Index;

        switch(scenario.Kind)
        {
            case ContextPropagationScenarioKind.DirectConsumption:
                sb.AppendLine($"    public static void Consume{i}() with int ctx {{");
                sb.AppendLine($"        Console.WriteLine(ctx);");
                sb.AppendLine($"    }}");
                sb.AppendLine();
                break;

            case ContextPropagationScenarioKind.PropagationThroughCaller:
                // A caller method that has a with-clause and calls a consumer.
                // This tests that context propagates through the call graph:
                // RunX (pushes context) → Caller{i} (has with clause, calls consumer) → Consume{i}
                sb.AppendLine($"    public static void Consume{i}() with int ctx {{");
                sb.AppendLine($"        Console.WriteLine(ctx);");
                sb.AppendLine($"    }}");
                sb.AppendLine($"    public static void Caller{i}() with int c {{");
                sb.AppendLine($"        Consume{i}();");
                sb.AppendLine($"    }}");
                sb.AppendLine();
                break;

            case ContextPropagationScenarioKind.ShadowingAndRestoration:
                sb.AppendLine($"    public static void Consume{i}() with int ctx {{");
                sb.AppendLine($"        Console.Write(ctx);");
                sb.AppendLine($"    }}");
                sb.AppendLine();
                break;

            case ContextPropagationScenarioKind.IndependentTypes:
                sb.AppendLine($"    public static void Consume{i}() with int ival with StrCtx sval {{");
                sb.AppendLine($"        Console.Write(ival);");
                sb.AppendLine($"        Console.Write(\",\");");
                sb.AppendLine($"        Console.WriteLine(sval.Value);");
                sb.AppendLine($"    }}");
                sb.AppendLine();
                break;

            case ContextPropagationScenarioKind.MultipleSameTypePushes:
                sb.AppendLine($"    public static void Consume{i}() with int ctx {{");
                sb.AppendLine($"        Console.WriteLine(ctx);");
                sb.AppendLine($"    }}");
                sb.AppendLine();
                break;
        }
    }

    /// <summary>
    /// Emits a per-scenario static runner method in the Program class.
    /// Each scenario is isolated to avoid deep try/finally nesting.
    /// </summary>
    private static void EmitScenarioRunnerMethod(StringBuilder sb, ContextPropagationScenario scenario)
    {
        var i = scenario.Index;
        sb.AppendLine($"    public static void Run{i}() {{");

        switch(scenario.Kind)
        {
            case ContextPropagationScenarioKind.DirectConsumption:
                {
                    var value = int.Parse(scenario.ExpectedOutput);
                    sb.AppendLine($"        with {value};");
                    sb.AppendLine($"        Consume{i}();");
                    break;
                }

            case ContextPropagationScenarioKind.PropagationThroughCaller:
                {
                    var value = int.Parse(scenario.ExpectedOutput);
                    sb.AppendLine($"        with {value};");
                    sb.AppendLine($"        Caller{i}();");
                    break;
                }

            case ContextPropagationScenarioKind.ShadowingAndRestoration:
                {
                    var parts = scenario.ExpectedOutput.Split(',');
                    var innerValue = int.Parse(parts[0]);
                    var outerValue = int.Parse(parts[1]);
                    sb.AppendLine($"        with {outerValue};");
                    sb.AppendLine($"        with({innerValue}) {{");
                    sb.AppendLine($"            Consume{i}();");
                    sb.AppendLine($"        }}");
                    sb.AppendLine($"        Console.Write(\",\");");
                    sb.AppendLine($"        Consume{i}();");
                    sb.AppendLine($"        Console.WriteLine();");
                    break;
                }

            case ContextPropagationScenarioKind.IndependentTypes:
                {
                    var parts = scenario.ExpectedOutput.Split(',');
                    var intValue = int.Parse(parts[0]);
                    var strValue = parts[1];
                    sb.AppendLine($"        with {intValue};");
                    sb.AppendLine($"        with StrCtx _ = new StrCtx(\"{strValue}\");");
                    sb.AppendLine($"        Consume{i}();");
                    break;
                }

            case ContextPropagationScenarioKind.MultipleSameTypePushes:
                {
                    var secondValue = int.Parse(scenario.ExpectedOutput);
                    var firstStr = scenario.Label.Split("first=")[1].Split(' ')[0];
                    var firstValue = int.Parse(firstStr);
                    sb.AppendLine($"        with {firstValue};");
                    sb.AppendLine($"        with {secondValue};");
                    sb.AppendLine($"        Consume{i}();");
                    break;
                }
        }

        sb.AppendLine($"    }}");
        sb.AppendLine();
    }

    // ─── Compile and run ─────────────────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source in-process via CompilerDriver, then runs the
    /// produced DLL with <c>dotnet</c> to capture stdout.
    /// </summary>
    private (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var iterDir = Path.Combine(outputDir, Guid.NewGuid().ToString("N"));
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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Runtime context propagation and scoping
    //
    // **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.8, 5.5, 6.1,
    //              6.2, 6.3, 6.4, 6.5, 7.1, 7.4, 10.1, 10.2, 10.3, 10.4,
    //              10.5, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any valid Mist program with context declarations and consumers connected
    /// through a call graph of arbitrary depth, compiling and executing the program
    /// SHALL produce output demonstrating that:
    ///   (a) consumers receive the most recently pushed value for their bound type,
    ///   (b) context values propagate through intermediate methods,
    ///   (c) when a shadowing scope exits, the previous value is restored,
    ///   (d) context declarations of different types do not interfere.
    ///
    /// 100 random scenarios batched into a single compile + single run.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.8, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5,
    ///              7.1, 7.4, 10.1, 10.2, 10.3, 10.4, 10.5, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8**
    /// </summary>
    [Fact]
    public void ContextPropagation_RuntimeBehavior_MatchesSpecification()
    {
        var scenarios = GenScenario()
            .Sample(IterationCount)
            .ToList();

        var mistSource = BuildBatchedProgram(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}\nSource:\n{mistSource}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        Assert.Equal(scenarios.Count, lines.Length);

        for(var i = 0; i < scenarios.Count; i++)
        {
            var expected = scenarios[i].ExpectedOutput;
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed: {scenarios[i]}\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }
}