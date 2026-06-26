// Feature: stack-only-types, Property 17: Span mismatch rejection
//
// **Validates: Requirements 7.7**
//
// In-process; assert differing span kind or non-reconcilable element types are
// rejected with a mismatch diagnostic (MIST_TC_0157) and the binding/call left
// unmodified.

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
/// The category of mismatch being tested.
/// </summary>
public enum SpanMismatchCategory
{
    /// <summary>Mutable Span assigned where ReadOnlySpan is expected (or vice versa).</summary>
    KindMismatch,
    /// <summary>Same span kind but incompatible element types.</summary>
    ElementTypeMismatch
}

/// <summary>
/// Describes a scenario where a span assignment is rejected due to kind mismatch
/// or element type mismatch, producing MIST_TC_0157.
/// </summary>
public sealed record SpanMismatchScenario(
    string Source,
    SpanMismatchCategory Category,
    string SourceSpanKind,
    string TargetSpanKind,
    string SourceElement,
    string TargetElement,
    string Description)
{
    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for span mismatch rejection
/// (Property 17: Span mismatch rejection).
///
/// Verifies that <see cref="TypeCheckerAnalyzer"/> rejects operations where
/// the span kind differs (mutable Span vs read-only ReadOnlySpan) or the
/// element types cannot be reconciled, emitting <c>MIST_TC_0157</c> with a
/// message identifying the specific mismatch reason. The binding is left
/// unmodified (the program is rejected with an error-severity diagnostic).
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// **Validates: Requirements 7.7**
/// </summary>
public class SpanMismatchRejectionPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Element type pools ──────────────────────────────────────────────

    private static readonly string[] StampableTypes =
        ["int", "uint", "long", "byte", "short", "float", "double", "bool", "char"];

    /// <summary>
    /// Pairs of element types that are NOT assignable to each other in either
    /// direction (no implicit conversion exists). Used for element-type mismatch
    /// scenarios. The <c>bool</c> type is incompatible with all numeric and char
    /// types in both directions.
    /// </summary>
    private static readonly (string Source, string Target)[] IncompatiblePairs =
    [
        ("bool", "int"),
        ("bool", "uint"),
        ("bool", "long"),
        ("bool", "byte"),
        ("bool", "short"),
        ("bool", "float"),
        ("bool", "double"),
        ("bool", "char"),
        ("int", "bool"),
        ("uint", "bool"),
        ("long", "bool"),
        ("byte", "bool"),
        ("short", "bool"),
        ("float", "bool"),
        ("double", "bool"),
        ("char", "bool"),
    ];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a mutable Span is assigned to a ReadOnlySpan binding.
    /// Expected: rejected with MIST_TC_0157 (kind mismatch).
    /// </summary>
    private static Gen<SpanMismatchScenario> GenKindMismatch_MutableToReadOnly()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 20)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram(
                   $"Span<{elementType}, unknown> s = stackalloc {elementType}[{size}];\n" +
                   $"    ReadOnlySpan<{elementType}, unknown> ro = s;")
               select new SpanMismatchScenario(
                   source,
                   SpanMismatchCategory.KindMismatch,
                   "Span", "ReadOnlySpan",
                   elementType, elementType,
                   $"Span<{elementType}> → ReadOnlySpan<{elementType}> (kind mismatch) → MIST_TC_0157");
    }

    /// <summary>
    /// Generates a scenario where a ReadOnlySpan is assigned to a mutable Span binding.
    /// Expected: rejected with MIST_TC_0157 (kind mismatch).
    /// </summary>
    private static Gen<SpanMismatchScenario> GenKindMismatch_ReadOnlyToMutable()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 20)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram(
                   $"ReadOnlySpan<{elementType}, unknown> ro = stackalloc {elementType}[{size}];\n" +
                   $"    Span<{elementType}, unknown> s = ro;")
               select new SpanMismatchScenario(
                   source,
                   SpanMismatchCategory.KindMismatch,
                   "ReadOnlySpan", "Span",
                   elementType, elementType,
                   $"ReadOnlySpan<{elementType}> → Span<{elementType}> (kind mismatch) → MIST_TC_0157");
    }

    /// <summary>
    /// Generates a scenario where a mutable Span with one element type is assigned
    /// to a mutable Span with an incompatible element type.
    /// Expected: rejected with MIST_TC_0157 (element type mismatch).
    /// </summary>
    private static Gen<SpanMismatchScenario> GenElementTypeMismatch_Mutable()
    {
        return from pairIdx in Gen.Choose(0, IncompatiblePairs.Length - 1)
               from size in Gen.Choose(1, 20)
               let pair = IncompatiblePairs[pairIdx]
               let source = WrapInProgram(
                   $"Span<{pair.Source}, unknown> src = stackalloc {pair.Source}[{size}];\n" +
                   $"    Span<{pair.Target}, unknown> dst = src;")
               select new SpanMismatchScenario(
                   source,
                   SpanMismatchCategory.ElementTypeMismatch,
                   "Span", "Span",
                   pair.Source, pair.Target,
                   $"Span<{pair.Source}> → Span<{pair.Target}> (element mismatch) → MIST_TC_0157");
    }

    /// <summary>
    /// Generates a scenario where a ReadOnlySpan with one element type is assigned
    /// to a ReadOnlySpan with an incompatible element type.
    /// Expected: rejected with MIST_TC_0157 (element type mismatch).
    /// </summary>
    private static Gen<SpanMismatchScenario> GenElementTypeMismatch_ReadOnly()
    {
        return from pairIdx in Gen.Choose(0, IncompatiblePairs.Length - 1)
               from size in Gen.Choose(1, 20)
               let pair = IncompatiblePairs[pairIdx]
               let source = WrapInProgram(
                   $"ReadOnlySpan<{pair.Source}, unknown> src = stackalloc {pair.Source}[{size}];\n" +
                   $"    ReadOnlySpan<{pair.Target}, unknown> dst = src;")
               select new SpanMismatchScenario(
                   source,
                   SpanMismatchCategory.ElementTypeMismatch,
                   "ReadOnlySpan", "ReadOnlySpan",
                   pair.Source, pair.Target,
                   $"ReadOnlySpan<{pair.Source}> → ReadOnlySpan<{pair.Target}> (element mismatch) → MIST_TC_0157");
    }

    /// <summary>
    /// Combined generator: all mismatch scenario types with equal probability.
    /// Every scenario is expected to be rejected with MIST_TC_0157.
    /// </summary>
    public static Arbitrary<SpanMismatchScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenKindMismatch_MutableToReadOnly(),
            GenKindMismatch_ReadOnlyToMutable(),
            GenElementTypeMismatch_Mutable(),
            GenElementTypeMismatch_ReadOnly()).ToArbitrary();
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string WrapInProgram(string body)
    {
        return $$"""
            class Program {
                static void Main() {
                    {{body}}
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
    // Property 17: Span mismatch rejection
    //
    // **Validates: Requirements 7.7**
    //
    // For any operation where the span kind differs (mutable Span vs read-only
    // ReadOnlySpan) or the Mist and CLR element types cannot be reconciled,
    // the operation is rejected with a mismatch diagnostic (MIST_TC_0157)
    // and the binding/call is left unmodified.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 17: A span assignment with differing kind or incompatible element types
    /// is always rejected with MIST_TC_0157. Verifies both that the specific diagnostic
    /// fires and that its message identifies the mismatch reason.
    ///
    /// **Validates: Requirements 7.7**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(SpanMismatchRejectionPropertyTests) })]
    public Property SpanMismatch_AlwaysRejectedWithMistTc0157(
        SpanMismatchScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var mismatchDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0157")
            .ToList();

        // Must have at least one MIST_TC_0157 diagnostic.
        var hasMismatchRejection = mismatchDiagnostics.Count >= 1;

        // Verify the message content based on the category.
        var messageIsCorrect = hasMismatchRejection && scenario.Category switch
        {
            SpanMismatchCategory.KindMismatch =>
                mismatchDiagnostics.Any(d =>
                    d.Message.Contains("kind mismatch", StringComparison.OrdinalIgnoreCase)),
            SpanMismatchCategory.ElementTypeMismatch =>
                mismatchDiagnostics.Any(d =>
                    d.Message.Contains("element type mismatch", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

        var passed = hasMismatchRejection && messageIsCorrect;

        return passed.ToProperty()
            .Label(
                $"Expected MIST_TC_0157 for span mismatch.\n" +
                $"Category={scenario.Category}\n" +
                $"Got {mismatchDiagnostics.Count} MIST_TC_0157 diagnostic(s).\n" +
                $"hasMismatchRejection={hasMismatchRejection}, messageIsCorrect={messageIsCorrect}\n" +
                $"Source span='{scenario.SourceSpanKind}<{scenario.SourceElement}>', " +
                $"Target span='{scenario.TargetSpanKind}<{scenario.TargetElement}>'\n" +
                $"Messages: [{string.Join("; ", mismatchDiagnostics.Select(d => d.Message))}]\n" +
                $"All errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Source:\n{scenario.Source}");
    }

    /// <summary>
    /// Property 17 (binding left unmodified): When a span mismatch is detected,
    /// the program is rejected with at least one error-severity diagnostic and no
    /// valid compilation output is produced — the binding is left unmodified.
    ///
    /// **Validates: Requirements 7.7**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(SpanMismatchRejectionPropertyTests) })]
    public Property SpanMismatch_ProgramIsRejected_BindingLeftUnmodified(
        SpanMismatchScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // The program must have at least one error-severity diagnostic
        // (indicating the program was rejected and the binding is unmodified).
        var hasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        // Among the errors, MIST_TC_0157 must be present.
        var hasMismatch = diagnostics.Any(d => d.Id.Value == "MIST_TC_0157");

        var passed = hasErrors && hasMismatch;

        return passed.ToProperty()
            .Label(
                $"Expected program to be rejected (errors present) with MIST_TC_0157.\n" +
                $"hasErrors={hasErrors}, hasMismatch={hasMismatch}\n" +
                $"Category={scenario.Category}\n" +
                $"All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}({d.Severity}): {d.Message}"))}]\n" +
                $"Source:\n{scenario.Source}");
    }
}