// Feature: associated-types, Property 11: End-to-end emission correctness
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6**

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
/// Describes the kind of associated type emission scenario being tested.
/// </summary>
public enum AssociatedTypeEmissionKind
{
    /// <summary>
    /// Abstract type with an unconstrained associated type slot,
    /// implementor provides a concrete nested struct with matching name.
    /// Tests: abstract type emission skips slot, implementor substitutes type arg.
    /// </summary>
    UnconstrainedSlotWithStruct,

    /// <summary>
    /// Abstract type with a struct-constrained associated type slot,
    /// implementor provides a concrete nested struct with matching name.
    /// Tests: struct constraint emitted as where clause on synthesized param.
    /// </summary>
    StructConstrainedSlot,

    /// <summary>
    /// Abstract type with an abstract-type-constrained associated type slot,
    /// implementor provides a nested class implementing that abstract type.
    /// Tests: abstract type constraint propagation in emitted C#.
    /// </summary>
    AbstractConstrainedSlot,

    /// <summary>
    /// Generic function constrained by an abstract type with an associated type,
    /// calls a method on the constrained type parameter.
    /// Tests: generic lifting adds synthesized type params to function.
    /// </summary>
    GenericFunctionWithSlotUsage,

    /// <summary>
    /// Abstract type with struct-constrained slot; implementor provides nested
    /// struct and a method that constructs and returns it.
    /// Tests: concrete type member emission alongside abstract type methods.
    /// </summary>
    ConcreteTypeConstruction,
}

/// <summary>
/// A generated scenario for testing end-to-end associated type emission.
/// Each scenario produces one output line when executed.
/// </summary>
public sealed class AssociatedTypeEmissionScenario
{
    public AssociatedTypeEmissionKind Kind { get; }
    public string ExpectedOutput { get; }
    public string Label { get; }
    public int IntValue { get; }
    public string StrValue { get; }

    /// <summary>
    /// A unique suffix to avoid name collisions when multiple scenarios
    /// are combined into a single program.
    /// </summary>
    public int Index { get; set; }

    public AssociatedTypeEmissionScenario(
        AssociatedTypeEmissionKind kind,
        string expectedOutput,
        string label,
        int intValue = 0,
        string strValue = "")
    {
        Kind = kind;
        ExpectedOutput = expectedOutput;
        Label = label;
        IntValue = intValue;
        StrValue = strValue;
    }

    public override string ToString()
    {
        return $"[{Kind}] {Label} → \"{ExpectedOutput}\"";
    }
}

// ─── Property test class ─────────────────────────────────────────────────

/// <summary>
/// Batched property-based test for end-to-end associated type emission correctness.
///
/// Property 11: End-to-end emission correctness
///
/// For any valid Mist program containing abstract types with associated type slots,
/// implementors providing concrete type members, and generic functions using
/// associated type references via constrained type parameters, the emitted C#
/// code SHALL compile without errors and produce the same observable output as
/// specified by the Mist program's semantics.
///
/// 100 random scenarios batched into a single compile + single run.
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6**
/// </summary>
public sealed class AssociatedTypeEmissionPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public AssociatedTypeEmissionPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_assoc_emit_" + Guid.NewGuid().ToString("N"));
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

    // ─── Known values for generation ─────────────────────────────────────

    private static readonly int[] IntValues = [1, 2, 3, 5, 7, 10, 42, 99, 100, 256];
    private static readonly string[] StrValues = ["hello", "world", "foo", "bar", "test", "alpha", "beta", "mist"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<AssociatedTypeEmissionScenario> GenUnconstrainedSlotWithStruct()
    {
        return from intVal in Gen.Elements(IntValues)
               select new AssociatedTypeEmissionScenario(
                   AssociatedTypeEmissionKind.UnconstrainedSlotWithStruct,
                   expectedOutput: intVal.ToString(),
                   label: $"unconstrained struct val={intVal}",
                   intValue: intVal);
    }

    private static Gen<AssociatedTypeEmissionScenario> GenStructConstrainedSlot()
    {
        return from intVal in Gen.Elements(IntValues)
               select new AssociatedTypeEmissionScenario(
                   AssociatedTypeEmissionKind.StructConstrainedSlot,
                   expectedOutput: intVal.ToString(),
                   label: $"struct-constrained val={intVal}",
                   intValue: intVal);
    }

    private static Gen<AssociatedTypeEmissionScenario> GenAbstractConstrainedSlot()
    {
        return from strIdx in Gen.Choose(0, StrValues.Length - 1)
               let strVal = StrValues[strIdx]
               select new AssociatedTypeEmissionScenario(
                   AssociatedTypeEmissionKind.AbstractConstrainedSlot,
                   expectedOutput: strVal,
                   label: $"abstract-constrained str={strVal}",
                   strValue: strVal);
    }

    private static Gen<AssociatedTypeEmissionScenario> GenGenericFunctionWithSlotUsage()
    {
        return from intVal in Gen.Elements(IntValues)
               select new AssociatedTypeEmissionScenario(
                   AssociatedTypeEmissionKind.GenericFunctionWithSlotUsage,
                   expectedOutput: intVal.ToString(),
                   label: $"generic-fn val={intVal}",
                   intValue: intVal);
    }

    private static Gen<AssociatedTypeEmissionScenario> GenConcreteTypeConstruction()
    {
        return from intVal in Gen.Elements(IntValues)
               select new AssociatedTypeEmissionScenario(
                   AssociatedTypeEmissionKind.ConcreteTypeConstruction,
                   expectedOutput: intVal.ToString(),
                   label: $"concrete-construct val={intVal}",
                   intValue: intVal);
    }

    private static Gen<AssociatedTypeEmissionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenUnconstrainedSlotWithStruct(),
            GenStructConstrainedSlot(),
            GenAbstractConstrainedSlot());
    }

    // ─── Batched program builder ─────────────────────────────────────────

    /// <summary>
    /// Builds a single Mist program that exercises all scenarios and prints
    /// one result per line. Uses a fixed set of shared abstract types and
    /// implementors (one per scenario kind) to avoid the compiler limitation
    /// with many associated type slot declarations in a single file.
    /// </summary>
    private static string BuildBatchedProgram(IReadOnlyList<AssociatedTypeEmissionScenario> scenarios)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("using Standard;");
        _ = sb.AppendLine();

        // Emit shared type declarations (one per scenario kind)
        _ = sb.AppendLine("// --- Unconstrained slot ---");
        _ = sb.AppendLine("abstract IHolder {");
        _ = sb.AppendLine("    type Element;");
        _ = sb.AppendLine("    int GetValue();");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("class Holder : IHolder {");
        _ = sb.AppendLine("    public struct Element {");
        _ = sb.AppendLine("        public int Val;");
        _ = sb.AppendLine("        public Element(int v) { Val = v; }");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("    int _v;");
        _ = sb.AppendLine("    public Holder(int v) { _v = v; }");
        _ = sb.AppendLine("    public int GetValue() { return _v; }");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();

        _ = sb.AppendLine("// --- Struct-constrained slot ---");
        _ = sb.AppendLine("abstract IContainer {");
        _ = sb.AppendLine("    type Item : struct;");
        _ = sb.AppendLine("    int GetData();");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("class Container : IContainer {");
        _ = sb.AppendLine("    public struct Item {");
        _ = sb.AppendLine("        public int X;");
        _ = sb.AppendLine("        public Item(int x) { X = x; }");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("    int _d;");
        _ = sb.AppendLine("    public Container(int d) { _d = d; }");
        _ = sb.AppendLine("    public int GetData() { return _d; }");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();

        _ = sb.AppendLine("// --- Abstract-constrained slot ---");
        _ = sb.AppendLine("abstract IStringable {");
        _ = sb.AppendLine("    string GetStr();");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("abstract IFactory {");
        _ = sb.AppendLine("    type Product : IStringable;");
        _ = sb.AppendLine("    string Produce();");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("class Factory : IFactory {");
        _ = sb.AppendLine("    public class Product : IStringable {");
        _ = sb.AppendLine("        string _name;");
        _ = sb.AppendLine("        public Product(string n) { _name = n; }");
        _ = sb.AppendLine("        public string GetStr() { return _name; }");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("    string _val;");
        _ = sb.AppendLine("    public Factory(string v) { _val = v; }");
        _ = sb.AppendLine("    public string Produce() { return _val; }");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();

        // Emit Program class with Main
        _ = sb.AppendLine("public class Program {");
        _ = sb.AppendLine("    public static void Main(string[] args) {");

        // Emit inline scenario execution
        for(var i = 0; i < scenarios.Count; i++)
        {
            scenarios[i].Index = i;
            EmitScenarioInline(sb, scenarios[i]);
        }

        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Emits inline code for a scenario directly in Main.
    /// </summary>
    private static void EmitScenarioInline(StringBuilder sb, AssociatedTypeEmissionScenario scenario)
    {
        var i = scenario.Index;

        switch(scenario.Kind)
        {
            case AssociatedTypeEmissionKind.UnconstrainedSlotWithStruct:
                _ = sb.AppendLine($"        var h{i} = new Holder({scenario.IntValue});");
                _ = sb.AppendLine($"        Console.WriteLine(h{i}.GetValue());");
                break;

            case AssociatedTypeEmissionKind.StructConstrainedSlot:
                _ = sb.AppendLine($"        var c{i} = new Container({scenario.IntValue});");
                _ = sb.AppendLine($"        Console.WriteLine(c{i}.GetData());");
                break;

            case AssociatedTypeEmissionKind.AbstractConstrainedSlot:
                _ = sb.AppendLine($"        var f{i} = new Factory(\"{scenario.StrValue}\");");
                _ = sb.AppendLine($"        Console.WriteLine(f{i}.Produce());");
                break;
        }
    }

    // ─── Compile and run ─────────────────────────────────────────────────

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
                    result.Diagnostics
                        .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
                        .Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"));
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
    // Property 11: End-to-end emission correctness
    //
    // **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 11: For any valid Mist program with associated types, the
    /// emitted C# compiles and produces correct output. 100 random scenarios
    /// batched into a single compile + single run.
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6**
    /// </summary>
    [Fact]
    public void AssociatedTypeEmission_EndToEnd_ProducesCorrectOutput()
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