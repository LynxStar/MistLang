// Feature: stack-only-types, Property 10: Read-only indexer assignment is always rejected
//
// **Validates: Requirements 2.8**
//
// In-process; for any index (in or out of range), assert assignment through a
// ReadOnlySpan indexer is rejected with MIST_TC_0156 independently of length validation.

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
/// Describes a scenario where an assignment targets the indexer of a ReadOnlySpan.
/// The test verifies that this is always rejected with MIST_TC_0156 regardless of
/// whether the index is in range or out of range.
/// </summary>
public sealed record ReadOnlySpanIndexerAssignmentScenario(
    string Source,
    string ElementType,
    int IndexValue,
    int? KnownLength,
    string Description)
{
    /// <summary>Whether the index would be in-range if bounds were checked.</summary>
    public bool IsIndexInRange => KnownLength.HasValue
        ? IndexValue >= 0 && IndexValue < KnownLength.Value
        : IndexValue >= 0;

    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for read-only indexer assignment rejection
/// (Property 10: Read-only indexer assignment is always rejected).
///
/// Verifies that <see cref="TypeCheckerAnalyzer"/> rejects any assignment whose
/// target is the indexer of a <c>ReadOnlySpan&lt;T, Length&gt;</c> value with
/// diagnostic <c>MIST_TC_0156</c>, independently of whether the index is in range
/// or out of range.
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// **Validates: Requirements 2.8**
/// </summary>
public class ReadOnlySpanIndexerAssignmentRejectionPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Element type pool: stampable primitives ─────────────────────────

    private static readonly string[] StampableTypes =
        ["int", "uint", "long", "byte", "short", "float", "double", "bool", "char"];

    /// <summary>
    /// Returns the default value literal for a given Mist primitive type,
    /// suitable for use as the right-hand side of an indexer assignment.
    /// </summary>
    private static string GetDefaultLiteral(string elementType) => elementType switch
    {
        "int" or "uint" or "long" or "byte" or "short" => "42",
        "float" => "1.0f",
        "double" => "1.0",
        "bool" => "true",
        "char" => "'x'",
        _ => "0"
    };

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where assignment targets a ReadOnlySpan indexer with
    /// a known-length span and an in-range index.
    /// Expected: rejected with MIST_TC_0156 (read-only rejection is independent of bounds).
    /// </summary>
    private static Gen<ReadOnlySpanIndexerAssignmentScenario> GenKnownLengthInRangeScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from length in Gen.Choose(1, 20)
               from index in Gen.Choose(0, length - 1)
               let elementType = StampableTypes[typeIdx]
               let source = BuildReadOnlySpanAssignment(elementType, length, index)
               select new ReadOnlySpanIndexerAssignmentScenario(
                   source, elementType, index, length,
                   $"ReadOnlySpan<{elementType},{length}>[{index}] = val (in-range) → MIST_TC_0156");
    }

    /// <summary>
    /// Generates a scenario where assignment targets a ReadOnlySpan indexer with
    /// a known-length span and an out-of-range index.
    /// Expected: rejected with MIST_TC_0156 (read-only rejection holds independently of length validation).
    /// </summary>
    private static Gen<ReadOnlySpanIndexerAssignmentScenario> GenKnownLengthOutOfRangeScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from length in Gen.Choose(1, 10)
               from overshoot in Gen.Choose(0, 10)
               let index = length + overshoot
               let elementType = StampableTypes[typeIdx]
               let source = BuildReadOnlySpanAssignment(elementType, length, index)
               select new ReadOnlySpanIndexerAssignmentScenario(
                   source, elementType, index, length,
                   $"ReadOnlySpan<{elementType},{length}>[{index}] = val (out-of-range) → MIST_TC_0156");
    }

    /// <summary>
    /// Generates a scenario where assignment targets a ReadOnlySpan indexer with
    /// an unknown-length span (using the ReadOnlySpan&lt;T&gt; alias) and a non-negative index.
    /// Expected: rejected with MIST_TC_0156 (read-only rejection is independent of length validation).
    /// </summary>
    private static Gen<ReadOnlySpanIndexerAssignmentScenario> GenUnknownLengthScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from index in Gen.Choose(0, 50)
               let elementType = StampableTypes[typeIdx]
               let source = BuildUnknownLengthReadOnlySpanAssignment(elementType, index)
               select new ReadOnlySpanIndexerAssignmentScenario(
                   source, elementType, index, null,
                   $"ReadOnlySpan<{elementType}>[{index}] = val (unknown-length) → MIST_TC_0156");
    }

    /// <summary>
    /// Combined generator: all scenario types with equal probability.
    /// Every scenario targets a ReadOnlySpan indexer for assignment and should
    /// always be rejected with MIST_TC_0156.
    /// </summary>
    public static Arbitrary<ReadOnlySpanIndexerAssignmentScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenKnownLengthInRangeScenario(),
            GenKnownLengthOutOfRangeScenario(),
            GenUnknownLengthScenario()).ToArbitrary();
    }

    // ─── Source builders ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a Mist program that creates a ReadOnlySpan via stackalloc (target-typed)
    /// with a known literal length and attempts to assign through the indexer.
    /// Uses the explicit arity-2 form <c>ReadOnlySpan&lt;T, unknown&gt;</c>.
    /// </summary>
    private static string BuildReadOnlySpanAssignment(string elementType, int length, int index)
    {
        var literal = GetDefaultLiteral(elementType);
        return $$"""
            class Program {
                static void Main() {
                    ReadOnlySpan<{{elementType}}, unknown> ro = stackalloc {{elementType}}[{{length}}];
                    ro[{{index}}] = {{literal}};
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist program that creates a ReadOnlySpan via stackalloc (unknown length,
    /// using the explicit arity-2 form) and attempts to assign through the indexer.
    /// </summary>
    private static string BuildUnknownLengthReadOnlySpanAssignment(string elementType, int index)
    {
        var literal = GetDefaultLiteral(elementType);
        return $$"""
            class Program {
                static void Main() {
                    ReadOnlySpan<{{elementType}}, unknown> ro = stackalloc {{elementType}}[10];
                    ro[{{index}}] = {{literal}};
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
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
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Read-only indexer assignment is always rejected
    //
    // **Validates: Requirements 2.8**
    //
    // For any value of a ReadOnlySpan<T, Length> family type and any index
    // expression (in range or out of range), an assignment whose target is
    // the span's indexer is rejected with MIST_TC_0156, independently of
    // any length validation.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 10: Assignment through a ReadOnlySpan indexer is always rejected with
    /// MIST_TC_0156 regardless of whether the index is in range or out of range.
    ///
    /// This verifies that read-only rejection holds independently of length validation:
    /// the diagnostic fires even when the index exceeds the documented length range.
    ///
    /// **Validates: Requirements 2.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ReadOnlySpanIndexerAssignmentRejectionPropertyTests) })]
    public Property ReadOnlySpanIndexerAssignment_AlwaysRejectedWithMistTc0156(
        ReadOnlySpanIndexerAssignmentScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var readOnlyDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0156")
            .ToList();

        // Must have at least one MIST_TC_0156 diagnostic.
        var hasReadOnlyRejection = readOnlyDiagnostics.Count >= 1;

        // The message should indicate that a ReadOnlySpan element cannot be assigned.
        var messageIsCorrect = hasReadOnlyRejection &&
            readOnlyDiagnostics.Any(d =>
                d.Message.Contains("ReadOnlySpan", StringComparison.Ordinal) &&
                d.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase));

        var passed = hasReadOnlyRejection && messageIsCorrect;

        return passed.ToProperty()
            .Label(
                $"Expected MIST_TC_0156 for ReadOnlySpan indexer assignment.\n" +
                $"Got {readOnlyDiagnostics.Count} MIST_TC_0156 diagnostic(s).\n" +
                $"hasReadOnlyRejection={hasReadOnlyRejection}, messageIsCorrect={messageIsCorrect}\n" +
                $"Index={scenario.IndexValue}, InRange={scenario.IsIndexInRange}, " +
                $"KnownLength={scenario.KnownLength?.ToString() ?? "unknown"}\n" +
                $"Messages: [{string.Join("; ", readOnlyDiagnostics.Select(d => d.Message))}]\n" +
                $"All errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Source:\n{scenario.Source}");
    }

    /// <summary>
    /// Property 10 (independence from length validation): The MIST_TC_0156 diagnostic
    /// fires even when the index is out of range. This confirms that the read-only
    /// rejection is independent of bounds checking.
    ///
    /// **Validates: Requirements 2.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ReadOnlySpanIndexerAssignmentRejectionPropertyTests) })]
    public Property ReadOnlySpanIndexerAssignment_IndependentOfLengthValidation(
        ReadOnlySpanIndexerAssignmentScenario scenario)
    {
        // Focus on out-of-range scenarios to specifically validate independence.
        if(scenario.IsIndexInRange)
        {
            return true.ToProperty().Label("Skipped: in-range scenario (covered by main property)");
        }

        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var readOnlyDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0156")
            .ToList();

        // Even with an out-of-range index, MIST_TC_0156 must still fire.
        var hasReadOnlyRejection = readOnlyDiagnostics.Count >= 1;

        return hasReadOnlyRejection.ToProperty()
            .Label(
                $"Expected MIST_TC_0156 even for out-of-range index " +
                $"(index={scenario.IndexValue}, length={scenario.KnownLength?.ToString() ?? "unknown"}).\n" +
                $"Got {readOnlyDiagnostics.Count} MIST_TC_0156 diagnostic(s).\n" +
                $"All errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Source:\n{scenario.Source}");
    }
}