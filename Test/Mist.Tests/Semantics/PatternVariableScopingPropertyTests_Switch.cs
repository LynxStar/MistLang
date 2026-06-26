// Feature: switch-expressions, Property 8: Pattern Variable Scoping
// **Validates: Requirements 4.9, 8.3, 8.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for pattern variable scoping in switch expression arms.
///
/// **Validates: Requirements 4.9, 8.3, 8.4**
///
/// For any switch arm whose pattern introduces a pattern variable (via type pattern or var pattern),
/// that variable SHALL be accessible with the correct type in both the arm's when guard expression
/// and the arm's result expression.
/// </summary>
public class PatternVariableScopingPropertyTests_Switch
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario: pattern variable used in guard and result expression.
    /// </summary>
    public sealed record PatternVarInGuardAndResultScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario: pattern variable used only in result expression (no guard).
    /// </summary>
    public sealed record PatternVarInResultOnlyScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] VarNames = ["n", "s", "v", "item", "val"];

    private static readonly (string ScrutineeType, string ScrutineeInit, string PatternType, string GuardExpr, string ResultExpr)[] TypePatternCases =
    [
        ("int", "42", "int", "{0} > 0", "{0}"),
        ("int", "10", "int", "{0} < 100", "{0}"),
        ("int", "5", "int", "{0} >= 0", "{0}"),
    ];

    private static readonly (string ScrutineeType, string ScrutineeInit, string ResultExpr)[] VarPatternCases =
    [
        ("int", "42", "{0}"),
        ("string", "\"hello\"", "{0}"),
        ("bool", "true", "{0}"),
    ];

    public static Arbitrary<PatternVarInGuardAndResultScenario> ArbitraryPatternVarInGuardAndResult()
    {
        var gen = from caseIdx in Gen.Choose(0, TypePatternCases.Length - 1)
                  from varName in Gen.Elements(VarNames)
                  let tc = TypePatternCases[caseIdx]
                  let guard = tc.GuardExpr.Replace("{0}", varName)
                  let result = tc.ResultExpr.Replace("{0}", varName)
                  let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        {tc.ScrutineeType} x = {tc.ScrutineeInit};\n        var r = x switch {{ {tc.PatternType} {varName} when {guard} => {result}, var _ => 0 }};\n    }}\n}}"
                  select new PatternVarInGuardAndResultScenario(source,
                      $"type pattern {tc.PatternType} {varName}, guard uses {varName}");
        return gen.ToArbitrary();
    }

    public static Arbitrary<PatternVarInResultOnlyScenario> ArbitraryPatternVarInResultOnly()
    {
        var gen = from caseIdx in Gen.Choose(0, VarPatternCases.Length - 1)
                  from varName in Gen.Elements(VarNames)
                  let tc = VarPatternCases[caseIdx]
                  let result = tc.ResultExpr.Replace("{0}", varName)
                  let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        {tc.ScrutineeType} x = {tc.ScrutineeInit};\n        var r = x switch {{ var {varName} => {result} }};\n    }}\n}}"
                  select new PatternVarInResultOnlyScenario(source,
                      $"var pattern {varName}, result uses {varName}");
        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8a: Pattern variable accessible in guard and result
    //
    // **Validates: Requirements 4.9, 8.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(PatternVariableScopingPropertyTests_Switch) })]
    public Property PatternVariable_AccessibleInGuardAndResult(PatternVarInGuardAndResultScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // No undefined identifier errors means the variable is accessible
        var hasUndefinedId = diagnostics.Any(d =>
            d.Id.Value == "MIST_SM_0001" || // undefined identifier
            d.Id.Value == "MIST_TC_0001");   // type mismatch

        return (!hasUndefinedId).ToProperty()
            .Label($"Pattern variable not accessible in guard/result. Scenario: {scenario.Label}. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8b: Pattern variable accessible in result (var pattern)
    //
    // **Validates: Requirements 4.9, 8.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(PatternVariableScopingPropertyTests_Switch) })]
    public Property PatternVariable_AccessibleInResult_VarPattern(PatternVarInResultOnlyScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // No undefined identifier errors means the variable is accessible
        var hasUndefinedId = diagnostics.Any(d =>
            d.Id.Value == "MIST_SM_0001" || // undefined identifier
            d.Id.Value == "MIST_TC_0001");   // type mismatch

        return (!hasUndefinedId).ToProperty()
            .Label($"Pattern variable not accessible in result. Scenario: {scenario.Label}. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}