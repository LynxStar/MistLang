// Feature: switch-expressions, Property 6: Guard Type Validation
// **Validates: Requirements 8.1, 8.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for guard type validation in switch expressions.
///
/// **Validates: Requirements 8.1, 8.2**
///
/// For any switch arm with a <c>when</c> guard whose condition does not evaluate to <c>bool</c>,
/// the type checker SHALL emit MIST_TC_0092. For guards that evaluate to <c>bool</c>,
/// no guard-related diagnostic SHALL be emitted.
/// </summary>
public class GuardTypeValidationPropertyTests
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
        _ = Directory.CreateDirectory(tempDir);
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
    /// Scenario: a valid bool guard expression.
    /// </summary>
    public sealed record ValidGuardScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario: an invalid non-bool guard expression.
    /// </summary>
    public sealed record InvalidGuardScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] BoolGuardExprs =
    [
        "true",
        "false",
        "n > 0",
        "n < 100",
        "n >= 0",
        "n <= 50",
        "n == 42",
        "n != 0",
    ];

    private static readonly string[] NonBoolGuardExprs =
    [
        "42",
        "\"hello\"",
        "n + 1",
        "n * 2",
        "n",
    ];

    public static Arbitrary<ValidGuardScenario> ArbitraryValidGuardScenario()
    {
        var gen = from guardExpr in Gen.Elements(BoolGuardExprs)
                  let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        int x = 42;\n        var r = x switch {{ int n when {guardExpr} => \"matched\", var _ => \"default\" }};\n    }}\n}}"
                  select new ValidGuardScenario(source, $"guard: {guardExpr}");
        return gen.ToArbitrary();
    }

    public static Arbitrary<InvalidGuardScenario> ArbitraryInvalidGuardScenario()
    {
        var gen = from guardExpr in Gen.Elements(NonBoolGuardExprs)
                  let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        int x = 42;\n        var r = x switch {{ int n when {guardExpr} => \"matched\", var _ => \"default\" }};\n    }}\n}}"
                  select new InvalidGuardScenario(source, $"guard: {guardExpr}");
        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6a: Valid bool guards produce no MIST_TC_0092
    //
    // **Validates: Requirements 8.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(GuardTypeValidationPropertyTests) })]
    public Property ValidBoolGuard_ProducesNoGuardDiagnostic(ValidGuardScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasGuardError = diagnostics.Any(d => d.Id.Value == "MIST_TC_0092");

        return (!hasGuardError).ToProperty()
            .Label($"Unexpected MIST_TC_0092 for valid guard. Scenario: {scenario.Label}. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6b: Non-bool guards produce MIST_TC_0092
    //
    // **Validates: Requirements 8.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(GuardTypeValidationPropertyTests) })]
    public Property NonBoolGuard_ProducesGuardDiagnostic(InvalidGuardScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasGuardError = diagnostics.Any(d => d.Id.Value == "MIST_TC_0092");

        return hasGuardError.ToProperty()
            .Label($"Expected MIST_TC_0092 for non-bool guard. Scenario: {scenario.Label}. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}