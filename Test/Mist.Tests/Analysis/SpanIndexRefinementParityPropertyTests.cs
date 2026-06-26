// Feature: stack-only-types, Property 11: Index refinement parity with arrays (incl. unknown-length runtime fallback)
//
// **Validates: Requirements 1.5, 2.5, 14.6**
//
// In-process, model-based against ArrayType: for identical length state and index,
// the span static-bounds outcome equals ValidateArrayIndexRefinement's outcome —
// full static check when length is a known constant, lower-bound-only with no
// upper-bound diagnostic when unknown.

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

/// <summary>
/// Property-based tests verifying that span indexing produces the same bounds-checking
/// diagnostics as array indexing for identical length state and index expression.
///
/// This is Property 11: Index refinement parity with arrays, model-based against ArrayType.
///
/// For any span kind K, length state L (known constant N or unknown), and index
/// expression i: the static bounds outcome of indexing a SpanType with length L is
/// identical to the outcome of ValidateArrayIndexRefinement for an ArrayType with the
/// same length L and index i.
///
/// - Known length N: index is fully statically checked (literal i accepted iff 0 &lt;= i &lt; N).
/// - Unknown length: only i &gt;= 0 is enforced statically; no upper-bound diagnostic is emitted.
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// **Validates: Requirements 1.5, 2.5, 14.6**
/// </summary>
public class SpanIndexRefinementParityPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Scenario model ──────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario pairing an array program and a span program with
    /// identical element type, length state, and index expression.
    /// </summary>
#pragma warning disable CA1034
    public sealed record IndexParityScenario(
        string ElementType,
        int? KnownLength,
        int IndexValue,
        bool IsReadOnly,
        string Description)
    {
        /// <summary>Whether the length is known (literal N) or unknown (runtime).</summary>
        public bool IsKnownLength => KnownLength.HasValue;

        /// <summary>
        /// The expected static bounds outcome for a literal index.
        /// When length is known: accepted iff 0 &lt;= IndexValue &lt; KnownLength.
        /// When length is unknown: accepted iff IndexValue &gt;= 0 (lower-bound-only).
        /// </summary>
        public bool ShouldAcceptIndex => KnownLength.HasValue
            ? IndexValue >= 0 && IndexValue < KnownLength.Value
            : IndexValue >= 0;

        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>Stampable primitive types that work with both arrays and stackalloc.</summary>
    private static readonly string[] StampableTypes =
        ["int", "uint", "long", "byte", "short", "float", "double", "bool", "char"];

    /// <summary>
    /// Generates a scenario with a known literal length (full static bounds checking).
    /// Uses only non-negative literal indices since the compiler's
    /// <c>TryExtractLiteralConstantValue</c> only recognizes positive integer literal
    /// tokens — unary negation expressions are treated as non-constants.
    /// Exercises both in-range (accepted) and out-of-range (rejected) indices.
    /// </summary>
    private static Gen<IndexParityScenario> GenKnownLengthScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from length in Gen.Choose(1, 20)
               from index in Gen.Choose(0, length + 5)
               from isReadOnly in Gen.Elements(false, true)
               let elementType = StampableTypes[typeIdx]
               select new IndexParityScenario(
                   elementType, length, index, isReadOnly,
                   $"{(isReadOnly ? "ReadOnlySpan" : "Span")}<{elementType},{length}>[{index}] vs {elementType}[{length}][{index}]");
    }

    /// <summary>
    /// Generates a scenario with unknown length (lower-bound-only static check).
    /// Non-negative literal indices are accepted (upper bound deferred to runtime);
    /// for the lower-bound test, only non-negative indices are generated since the
    /// compiler can only statically evaluate positive integer literals.
    /// </summary>
    private static Gen<IndexParityScenario> GenUnknownLengthScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from index in Gen.Choose(0, 50)
               from isReadOnly in Gen.Elements(false, true)
               let elementType = StampableTypes[typeIdx]
               select new IndexParityScenario(
                   elementType, null, index, isReadOnly,
                   $"{(isReadOnly ? "ReadOnlySpan" : "Span")}<{elementType},unknown>[{index}] vs {elementType}[][{index}]");
    }

    /// <summary>
    /// Combined generator: known and unknown lengths with equal probability.
    /// </summary>
    public static Arbitrary<IndexParityScenario> ArbitraryIndexParityScenario()
    {
        return Gen.OneOf(
            GenKnownLengthScenario(),
            GenUnknownLengthScenario()).ToArbitrary();
    }

    // ─── Source builders ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a Mist program that allocates a span via stackalloc and indexes it.
    /// For known length: <c>var s = stackalloc T[N]; var x = s[index];</c>
    /// For unknown length: uses a runtime variable for size to get the unknown-length
    /// form, or uses an explicit <c>Span&lt;T&gt;</c> (unknown alias) target.
    /// </summary>
    private static string BuildSpanProgram(IndexParityScenario scenario)
    {
        var spanKeyword = scenario.IsReadOnly ? "ReadOnlySpan" : "Span";

        if(scenario.IsKnownLength)
        {
            // Known length: literal size → SpanType(T, N)
            // Use `var` to keep the known-length type.
            return $$"""
                class Program {
                    static void Main() {
                        var s = stackalloc {{scenario.ElementType}}[{{scenario.KnownLength}}];
                        var x = s[{{scenario.IndexValue}}];
                    }
                }
                """;
        }
        else
        {
            // Unknown length: use the Span<T> / ReadOnlySpan<T> alias (unknown length).
            // Assigning a known-length stackalloc result to the alias widens to unknown.
            return $$"""
                class Program {
                    static void Main() {
                        {{spanKeyword}}<{{scenario.ElementType}}> s = stackalloc {{scenario.ElementType}}[10];
                        var x = s[{{scenario.IndexValue}}];
                    }
                }
                """;
        }
    }

    /// <summary>
    /// Builds a Mist program that allocates an array and indexes it.
    /// For known length: <c>var a = new T[N]; var x = a[index];</c> — the known-length
    /// array type is preserved via <c>var</c>.
    /// For unknown length: <c>T[] a = new T[10]; var x = a[index];</c> — the explicit
    /// <c>T[]</c> annotation widens to unknown length (matching array precedent).
    /// </summary>
    private static string BuildArrayProgram(IndexParityScenario scenario)
    {
        if(scenario.IsKnownLength)
        {
            // Known length: literal size → ArrayType(T, N)
            return $$"""
                class Program {
                    static void Main() {
                        var a = new {{scenario.ElementType}}[{{scenario.KnownLength}}];
                        var x = a[{{scenario.IndexValue}}];
                    }
                }
                """;
        }
        else
        {
            // Unknown length: explicit T[] annotation (erases known length).
            return $$"""
                class Program {
                    static void Main() {
                        {{scenario.ElementType}}[] a = new {{scenario.ElementType}}[10];
                        var x = a[{{scenario.IndexValue}}];
                    }
                }
                """;
        }
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

    /// <summary>
    /// Extracts bounds-related diagnostics from a compilation result.
    /// Filters to only MIST_REFINEMENT_0005 (ParameterRefinementViolation) which is
    /// the diagnostic emitted by ValidateLengthIndexRefinement for index bounds violations.
    /// </summary>
    private static List<Diagnostic> ExtractBoundsDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Where(d => d.Id == TypeCheckerAnalyzer.ParameterRefinementViolation)
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Index refinement parity with arrays
    //
    // For identical length state and index, the span static-bounds outcome
    // equals the array static-bounds outcome.
    //
    // **Validates: Requirements 1.5, 2.5, 14.6**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 11: For any span kind, length state, and literal index, the bounds
    /// diagnostic outcome of indexing a span matches that of indexing an array with
    /// the same length state and index.
    ///
    /// When length is a known constant N:
    /// - Both span and array accept the index iff 0 &lt;= i &lt; N (no bounds diagnostic).
    /// - Both reject with MIST_REFINEMENT_0005 when i &lt; 0 or i &gt;= N.
    ///
    /// When length is unknown:
    /// - Both accept the index iff i &gt;= 0 (lower-bound-only static check).
    /// - Both reject with MIST_REFINEMENT_0005 when i &lt; 0.
    /// - Neither emits an upper-bound diagnostic (deferred to runtime).
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(SpanIndexRefinementParityPropertyTests) })]
    public Property SpanAndArray_ProduceSameBoundsDiagnostics(IndexParityScenario scenario)
    {
        var spanSource = BuildSpanProgram(scenario);
        var arraySource = BuildArrayProgram(scenario);

        var spanDiagnostics = CompileAndGetDiagnostics(spanSource);
        var arrayDiagnostics = CompileAndGetDiagnostics(arraySource);

        var spanBounds = ExtractBoundsDiagnostics(spanDiagnostics);
        var arrayBounds = ExtractBoundsDiagnostics(arrayDiagnostics);

        // Core parity assertion: same number of bounds diagnostics.
        var sameDiagnosticCount = spanBounds.Count == arrayBounds.Count;

        // Secondary assertion: matches expected behavior.
        var expectedViolations = scenario.ShouldAcceptIndex ? 0 : 1;
        var spanMatchesExpected = spanBounds.Count == expectedViolations;
        var arrayMatchesExpected = arrayBounds.Count == expectedViolations;

        var passed = sameDiagnosticCount && spanMatchesExpected && arrayMatchesExpected;

        return passed.ToProperty()
            .Label(
                $"Parity check: span bounds={spanBounds.Count}, array bounds={arrayBounds.Count}, " +
                $"expected={expectedViolations}\n" +
                $"Scenario: {scenario}\n" +
                $"Span all diags: [{string.Join(", ", spanDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Array all diags: [{string.Join(", ", arrayDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Span source:\n{spanSource}\n" +
                $"Array source:\n{arraySource}");
    }

    /// <summary>
    /// Property 11 (unknown-length specific): When length is unknown and the literal
    /// index is non-negative, NO upper-bound diagnostic is emitted for either span or
    /// array — verifying the intentional runtime-fallback behavior (R14.6).
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(SpanIndexRefinementParityPropertyTests) })]
    public Property UnknownLength_NeverEmitsUpperBoundDiagnostic(IndexParityScenario scenario)
    {
        // All unknown-length scenarios have non-negative index by construction.
        if(scenario.IsKnownLength)
            return true.ToProperty().Label("Skipped: not an unknown-length scenario");

        var spanSource = BuildSpanProgram(scenario);
        var arraySource = BuildArrayProgram(scenario);

        var spanDiagnostics = CompileAndGetDiagnostics(spanSource);
        var arrayDiagnostics = CompileAndGetDiagnostics(arraySource);

        var spanBounds = ExtractBoundsDiagnostics(spanDiagnostics);
        var arrayBounds = ExtractBoundsDiagnostics(arrayDiagnostics);

        // With unknown length and i >= 0, no bounds diagnostic should fire.
        // The upper bound is deferred to runtime (R14.6).
        var passed = spanBounds.Count == 0 && arrayBounds.Count == 0;

        return passed.ToProperty()
            .Label(
                $"Unknown-length runtime fallback violated: span bounds={spanBounds.Count}, " +
                $"array bounds={arrayBounds.Count} (expected 0 for both)\n" +
                $"Scenario: {scenario}\n" +
                $"Span diags: [{string.Join(", ", spanDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Array diags: [{string.Join(", ", arrayDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}