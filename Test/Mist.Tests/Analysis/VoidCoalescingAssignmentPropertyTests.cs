// Feature: optional-types, Properties 14, 15, 16: Void-coalescing assignment type checking
// **Validates: Requirements 5.1, 5.4, 5.6, 5.7, 5.8**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
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

namespace Mist.Tests.Analysis;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Represents a scenario for Property 14: ??= applied to an invalid target.
/// </summary>
public sealed class InvalidAssignmentTargetScenario
{
    public string Source { get; }
    public string ExpectedDiagnosticId { get; }
    public string Label { get; }

    public InvalidAssignmentTargetScenario(string source, string expectedDiagnosticId, string label)
    {
        Source = source;
        ExpectedDiagnosticId = expectedDiagnosticId;
        Label = label;
    }

    public override string ToString()
    {
        return Label;
    }
}

/// <summary>
/// Represents a scenario for Property 15: flow narrowing after ??=.
/// </summary>
public sealed class FlowNarrowingScenario
{
    public string Source { get; }
    public string Label { get; }

    public FlowNarrowingScenario(string source, string label)
    {
        Source = source;
        Label = label;
    }

    public override string ToString()
    {
        return Label;
    }
}

/// <summary>
/// Represents a scenario for Property 16: ??= used as sub-expression.
/// </summary>
public sealed class StatementOnlyScenario
{
    public string Source { get; }
    public string Label { get; }

    public StatementOnlyScenario(string source, string label)
    {
        Source = source;
        Label = label;
    }

    public override string ToString()
    {
        return Label;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for void-coalescing assignment (??=) type checking.
///
/// Property 14: ??= on readonly/non-variable/non-optional produces errors.
/// Property 15: After x ??= value, x has type T (not T | void).
/// Property 16: Using ??= as sub-expression produces MIST_TC_0047.
///
/// **Validates: Requirements 5.1, 5.4, 5.6, 5.7, 5.8**
/// </summary>
public sealed class VoidCoalescingAssignmentPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
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

    // ─── Known types for generation ─────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "double"];

    private static string DefaultValueFor(string type)
    {
        return type switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            "float" => "3",
            "double" => "2.71",
            _ => "0",
        };
    }

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios where ??= is applied to a readonly variable (MIST_TC_0043).
    /// </summary>
    private static Gen<InvalidAssignmentTargetScenario> GenReadonlyTarget()
    {
        return Gen.Elements(PrimitiveTypes).Select(t =>
        {
            var source = $$"""
                class Program {
                    public static void Main() {
                        readonly {{t}}? x = {{DefaultValueFor(t)}};
                        x ??= {{DefaultValueFor(t)}};
                    }
                }
                """;
            return new InvalidAssignmentTargetScenario(source, "MIST_TC_0043", $"readonly {t}? x; x ??= ...");
        });
    }

    /// <summary>
    /// Generates scenarios where ??= is applied to a non-optional variable (MIST_TC_0042).
    /// </summary>
    private static Gen<InvalidAssignmentTargetScenario> GenNonOptionalTarget()
    {
        return Gen.Elements(PrimitiveTypes).Select(t =>
        {
            var source = $$"""
                class Program {
                    public static void Main() {
                        {{t}} x = {{DefaultValueFor(t)}};
                        x ??= {{DefaultValueFor(t)}};
                    }
                }
                """;
            return new InvalidAssignmentTargetScenario(source, "MIST_TC_0042", $"{t} x; x ??= ...");
        });
    }

    /// <summary>
    /// Generates combined scenarios for Property 14.
    /// </summary>
    public static Arbitrary<InvalidAssignmentTargetScenario> ArbitraryInvalidAssignmentTargetScenario()
    {
        var gen = Gen.Frequency(
            (1, GenReadonlyTarget()),
            (1, GenNonOptionalTarget()));
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios for Property 15: flow narrowing after ??=.
    /// After x ??= value, x should have type T (not T | void), so assigning
    /// x to a variable of type T should succeed without errors.
    /// </summary>
    public static Arbitrary<FlowNarrowingScenario> ArbitraryFlowNarrowingScenario()
    {
        var gen = Gen.Elements(PrimitiveTypes).Select(t =>
        {
            var source = $$"""
                class Program {
                    public static void Main() {
                        {{t}}? x = {{DefaultValueFor(t)}};
                        x ??= {{DefaultValueFor(t)}};
                        {{t}} narrowed = x;
                    }
                }
                """;
            return new FlowNarrowingScenario(source, $"{t}? x; x ??= ...; {t} narrowed = x");
        });
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios for Property 16: ??= used as sub-expression.
    /// </summary>
    public static Arbitrary<StatementOnlyScenario> ArbitraryStatementOnlyScenario()
    {
        var gen = Gen.Elements(PrimitiveTypes).Select(t =>
        {
            var source = $$"""
                class Program {
                    public static void Main() {
                        {{t}}? x = {{DefaultValueFor(t)}};
                        var y = (x ??= {{DefaultValueFor(t)}});
                    }
                }
                """;
            return new StatementOnlyScenario(source, $"var y = (x ??= {DefaultValueFor(t)})");
        });
        return gen.ToArbitrary();
    }

    // ─── Property 14: ??= requires mutable optional variable ────────────

    /// <summary>
    /// Property 14: Void-coalescing assignment requires mutable optional variable.
    /// ??= on readonly/non-optional produces the expected diagnostic error.
    ///
    /// - Readonly variable → MIST_TC_0043
    /// - Non-optional variable → MIST_TC_0042
    ///
    /// **Validates: Requirements 5.1, 5.6, 5.7**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(VoidCoalescingAssignmentPropertyTests) })]
    public Property InvalidTarget_ProducesExpectedDiagnostic(InvalidAssignmentTargetScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasExpectedDiagnostic = diagnostics.Any(d =>
            d.Id.Value == scenario.ExpectedDiagnosticId &&
            d.Severity == DiagnosticSeverity.Error);

        return hasExpectedDiagnostic.ToProperty()
            .Label($"Expected {scenario.ExpectedDiagnosticId} for '{scenario.Label}' but it was not emitted")
            .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ─── Property 15: Flow narrowing after ??= ──────────────────────────

    /// <summary>
    /// Property 15: Void-coalescing assignment flow narrowing.
    /// After x ??= value, x has type T (not T | void). Assigning x to a
    /// variable of type T (without void) in subsequent statements produces
    /// no type-checking errors.
    ///
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(VoidCoalescingAssignmentPropertyTests) })]
    public Property FlowNarrowing_AfterCoalescingAssignment_NoErrors(FlowNarrowingScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // Filter out diagnostics from the Standard library
        var projectDiagnostics = diagnostics
            .Where(d => !d.CompilationUnitPath.Contains("Standard/"))
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var noErrors = projectDiagnostics.Count == 0;

        return noErrors.ToProperty()
            .Label($"Expected no errors for '{scenario.Label}' but got: [{string.Join(", ", projectDiagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ─── Property 16: ??= is statement-only ─────────────────────────────

    /// <summary>
    /// Property 16: Void-coalescing assignment is statement-only.
    /// Using ??= as sub-expression produces MIST_TC_0047.
    ///
    /// **Validates: Requirements 5.8**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(VoidCoalescingAssignmentPropertyTests) })]
    public Property StatementOnly_SubExpression_ProducesDiagnostic(StatementOnlyScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMistTc0047 = diagnostics.Any(d =>
            d.Id.Value == "MIST_TC_0047" &&
            d.Severity == DiagnosticSeverity.Error);

        return hasMistTc0047.ToProperty()
            .Label($"Expected MIST_TC_0047 for '{scenario.Label}' but it was not emitted")
            .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}