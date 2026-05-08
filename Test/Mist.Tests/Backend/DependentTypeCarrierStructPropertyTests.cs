// Feature: dependent-types, Property 7: Carrier Struct Uniqueness
// **Validates: Requirements 13.3, 13.6**

using System.Collections.Immutable;
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

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based test verifying carrier struct uniqueness:
///
/// Property 7: Carrier Struct Uniqueness
/// For any set of dependent type argument usages in a program, the emitter SHALL produce
/// exactly one carrier struct per unique (primitive type, value) pair. Two usages of the
/// same value and type SHALL share the same carrier struct.
///
/// Approach:
/// 1. Generate random programs that use the same dependent value multiple times
///    (e.g., class A : Base&lt;5&gt; { } and class B : Base&lt;5&gt; { })
/// 2. Compile in-process via CompilerDriver (no process spawning)
/// 3. Inspect the emitted DependentTypeHelpers.g.cs file
/// 4. Count occurrences of each carrier struct definition
/// 5. Verify each unique (type, value) pair appears exactly once
///
/// Process spawns: 0 (entirely in-process).
///
/// **Validates: Requirements 13.3, 13.6**
/// </summary>
public sealed class DependentTypeCarrierStructPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Scenario model ──────────────────────────────────────────────────

    private sealed class CarrierStructScenario
    {
        public required string MistSource { get; init; }
        public required List<ExpectedCarrier> ExpectedCarriers { get; init; }
        public required string Description { get; init; }
    }

    private sealed class ExpectedCarrier
    {
        public required string StructName { get; init; }
    }

    // ─── Value pools ─────────────────────────────────────────────────────

    private static readonly string[] IntValues = ["0", "1", "5", "10", "42", "100"];
    private static readonly string[] BoolValues = ["true", "false"];
    private static readonly string[] CharValues = ["'A'", "'x'", "'Z'"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where the same int value is used multiple times
    /// across different classes. Expects exactly one carrier struct for that value.
    /// </summary>
    private static Gen<CarrierStructScenario> GenRepeatedIntValue()
    {
        return from value in Gen.Elements(IntValues)
               from numUsages in Gen.Choose(2, 4)
               from idx in Gen.Choose(0, 999)
               let baseName = $"IntBase{idx}"
               let source = BuildRepeatedUsageSource(baseName, "int", value, numUsages)
               let carrierName = $"Const_Int_{value}"
               select new CarrierStructScenario
               {
                   MistSource = source,
                   ExpectedCarriers = [new ExpectedCarrier { StructName = carrierName }],
                   Description = $"int value {value} used {numUsages} times",
               };
    }

    /// <summary>
    /// Generates a scenario where the same bool value is used multiple times.
    /// </summary>
    private static Gen<CarrierStructScenario> GenRepeatedBoolValue()
    {
        return from value in Gen.Elements(BoolValues)
               from numUsages in Gen.Choose(2, 4)
               from idx in Gen.Choose(0, 999)
               let baseName = $"BoolBase{idx}"
               let source = BuildRepeatedUsageSource(baseName, "bool", value, numUsages)
               let displayValue = value == "true" ? "True" : "False"
               let carrierName = $"Const_Bool_{displayValue}"
               select new CarrierStructScenario
               {
                   MistSource = source,
                   ExpectedCarriers = [new ExpectedCarrier { StructName = carrierName }],
                   Description = $"bool value {value} used {numUsages} times",
               };
    }

    /// <summary>
    /// Generates a scenario where the same char value is used multiple times.
    /// </summary>
    private static Gen<CarrierStructScenario> GenRepeatedCharValue()
    {
        return from value in Gen.Elements(CharValues)
               from numUsages in Gen.Choose(2, 4)
               from idx in Gen.Choose(0, 999)
               let baseName = $"CharBase{idx}"
               let source = BuildRepeatedUsageSource(baseName, "char", value, numUsages)
               let charLetter = value[1..^1]
               let carrierName = $"Const_Char_{charLetter}"
               select new CarrierStructScenario
               {
                   MistSource = source,
                   ExpectedCarriers = [new ExpectedCarrier { StructName = carrierName }],
                   Description = $"char value {value} used {numUsages} times",
               };
    }

    /// <summary>
    /// Generates a scenario with multiple distinct int values, each used multiple times.
    /// Expects one carrier struct per distinct value.
    /// </summary>
    private static Gen<CarrierStructScenario> GenMultipleDistinctIntValues()
    {
        return from value1 in Gen.Elements("1", "5", "10")
               from value2 in Gen.Elements("42", "100", "0")
               from idx in Gen.Choose(0, 999)
               let baseName1 = $"MultiA{idx}"
               let baseName2 = $"MultiB{idx}"
               let source = $"class {baseName1}<Size:int> {{ }}\n" +
                            $"class Use1{baseName1} : {baseName1}<{value1}> {{ }}\n" +
                            $"class Use2{baseName1} : {baseName1}<{value1}> {{ }}\n" +
                            $"class {baseName2}<N:int> {{ }}\n" +
                            $"class Use1{baseName2} : {baseName2}<{value2}> {{ }}\n" +
                            $"class Use2{baseName2} : {baseName2}<{value2}> {{ }}\n" +
                            "class Program { public static void Main() { } }\n"
               select new CarrierStructScenario
               {
                   MistSource = source,
                   ExpectedCarriers =
                   [
                       new ExpectedCarrier { StructName = $"Const_Int_{value1}" },
                       new ExpectedCarrier { StructName = $"Const_Int_{value2}" },
                   ],
                   Description = $"int values {value1} and {value2} each used twice",
               };
    }

    /// <summary>
    /// Generates a scenario where the same value is used across different base types.
    /// The carrier struct should still be deduplicated.
    /// </summary>
    private static Gen<CarrierStructScenario> GenSameValueDifferentBaseTypes()
    {
        return from value in Gen.Elements("5", "10", "42")
               from idx in Gen.Choose(0, 999)
               let typeA = $"TypeA{idx}"
               let typeB = $"TypeB{idx}"
               let source = $"class {typeA}<Size:int> {{ }}\n" +
                            $"class {typeB}<Cap:int> {{ }}\n" +
                            $"class UseA{idx} : {typeA}<{value}> {{ }}\n" +
                            $"class UseB{idx} : {typeB}<{value}> {{ }}\n" +
                            "class Program { public static void Main() { } }\n"
               let carrierName = $"Const_Int_{value}"
               select new CarrierStructScenario
               {
                   MistSource = source,
                   ExpectedCarriers = [new ExpectedCarrier { StructName = carrierName }],
                   Description = $"int value {value} used across different base types",
               };
    }

    /// <summary>Combined generator producing any scenario kind.</summary>
    private static Gen<CarrierStructScenario> GenScenario()
    {
        return Gen.OneOf(
            GenRepeatedIntValue(),
            GenRepeatedBoolValue(),
            GenRepeatedCharValue(),
            GenMultipleDistinctIntValues(),
            GenSameValueDifferentBaseTypes());
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string BuildRepeatedUsageSource(
        string baseName, string valueType, string value, int numUsages)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"class {baseName}<P:{valueType}> {{ }}");
        for (var i = 0; i < numUsages; i++)
        {
            sb.AppendLine($"class Use{i}{baseName} : {baseName}<{value}> {{ }}");
        }

        sb.AppendLine("class Program { public static void Main() { } }");
        return sb.ToString();
    }

    /// <summary>
    /// Compiles a Mist source in-process and returns the emitted files.
    /// </summary>
    private static (IReadOnlyDictionary<string, string>? Files, string? Error) CompileInProcess(
        string mistSource)
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "mist_carrier_prop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);

        try
        {
            var sourcePath = Path.Combine(projectDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var options = new CompilerOptions(
                ProjectFolder: projectDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: projectDir,
                BinDirectory: Path.Combine(projectDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);

            if (!result.Succeeded)
            {
                var diagnosticText = string.Join("\n",
                    result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => $"  {d.Id.Value}: {d.Message}"));
                return (null, diagnosticText);
            }

            return (result.EmittedFiles, null);
        }
        finally
        {
            try { Directory.Delete(projectDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Counts occurrences of a carrier struct definition in the helpers file content.
    /// Matches "public struct {name} :" pattern.
    /// </summary>
    private static int CountCarrierStructDefinitions(string helpersContent, string structName)
    {
        var pattern = $"public struct {Regex.Escape(structName)} :";
        return Regex.Matches(helpersContent, pattern).Count;
    }

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 7: Carrier Struct Uniqueness
    ///
    /// For any set of dependent type argument usages in a program, the emitter SHALL
    /// produce exactly one carrier struct per unique (primitive type, value) pair.
    /// Two usages of the same value and type SHALL share the same carrier struct.
    ///
    /// Process spawns: 0 (entirely in-process).
    ///
    /// **Validates: Requirements 13.3, 13.6**
    /// </summary>
    [Fact]
    public void CarrierStructs_AreUnique_PerTypeValuePair()
    {
        // Generate 100 random scenarios using FsCheck
        var scenarios = GenScenario().Sample(100).ToList();

        var failures = new List<string>();

        for (var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var (files, error) = CompileInProcess(scenario.MistSource);

            if (files is null)
            {
                failures.Add($"Scenario {i} ({scenario.Description}): compilation failed:\n{error}");
                continue;
            }

            if (!files.TryGetValue("DependentTypeHelpers.g.cs", out var helpersContent))
            {
                failures.Add($"Scenario {i} ({scenario.Description}): DependentTypeHelpers.g.cs not emitted");
                continue;
            }

            foreach (var expected in scenario.ExpectedCarriers)
            {
                var count = CountCarrierStructDefinitions(helpersContent, expected.StructName);
                if (count != 1)
                {
                    failures.Add(
                        $"Scenario {i} ({scenario.Description}): " +
                        $"expected exactly 1 definition of '{expected.StructName}', found {count}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"Carrier struct uniqueness violated in {failures.Count} scenario(s):\n" +
            string.Join("\n", failures.Take(10)));
    }
}
