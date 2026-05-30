// Feature: tuple-pattern-matching, Property 5: Recursive element type validation
// **Validates: Requirements 4.3, 5.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for recursive element type validation in tuple positional patterns.
///
/// **Validates: Requirements 4.3, 5.2**
///
/// Property 5: For any tuple type with arity N and a tuple positional pattern with N elements
/// (including nested tuple positional patterns at any depth), the type checker SHALL validate
/// each element subpattern against the corresponding element type of the tuple, recursively
/// applying tuple validation when an element type is itself a TupleType and the subpattern
/// is a TuplePositionalPattern.
/// </summary>
public class TuplePositionalPatternElementValidationPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if (parseResult.Root is null)
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a scenario with a flat tuple where some elements have
    /// type-incompatible constant patterns (string literal against int element).
    /// </summary>
    public record FlatElementValidationScenario(
        string TupleType,
        string TupleValue,
        string Pattern,
        int InvalidElementCount)
    {
        public override string ToString() =>
            $"{TupleType} with pattern {Pattern} ({InvalidElementCount} invalid)";
    }

    /// <summary>
    /// Represents a scenario with a nested tuple where the inner tuple pattern
    /// has type-incompatible elements.
    /// </summary>
    public record NestedElementValidationScenario(
        string TupleType,
        string TupleValue,
        string Pattern,
        int InvalidElementCount)
    {
        public override string ToString() =>
            $"{TupleType} with pattern {Pattern} ({InvalidElementCount} invalid)";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Primitive types that can be used as tuple elements.
    /// Each entry is (type keyword, valid constant literal, incompatible constant literal).
    /// </summary>
    private static readonly (string Type, string ValidLiteral, string IncompatibleLiteral)[] ElementTypes =
    [
        ("int", "42", "\"bad\""),
        ("string", "\"hello\"", "42"),
        ("bool", "true", "42"),
        ("double", "3.14", "\"bad\""),
    ];

    /// <summary>
    /// Generates a flat tuple scenario with arity 2–5 where a random subset
    /// of elements have type-incompatible constant patterns.
    /// </summary>
    public static Arbitrary<FlatElementValidationScenario> ArbitraryFlatScenario()
    {
        var gen =
            from arity in Gen.Choose(2, 5)
            from typeIndices in Gen.ArrayOf(Gen.Choose(0, ElementTypes.Length - 1), arity)
            from invalidMask in GenInvalidMask(arity)
            let elements = typeIndices.Select((ti, i) => (
                Type: ElementTypes[ti].Type,
                Value: ElementTypes[ti].ValidLiteral,
                PatternLiteral: invalidMask[i]
                    ? ElementTypes[ti].IncompatibleLiteral
                    : ElementTypes[ti].ValidLiteral
            )).ToArray()
            let tupleType = $"({string.Join(", ", elements.Select(e => e.Type))})"
            let tupleValue = $"({string.Join(", ", elements.Select(e => e.Value))})"
            let pattern = $"({string.Join(", ", elements.Select(e => e.PatternLiteral))})"
            let invalidCount = invalidMask.Count(m => m)
            select new FlatElementValidationScenario(tupleType, tupleValue, pattern, invalidCount);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a nested tuple scenario where the outer tuple has an inner tuple element,
    /// and the inner pattern has type-incompatible elements.
    /// </summary>
    public static Arbitrary<NestedElementValidationScenario> ArbitraryNestedScenario()
    {
        var gen =
            from innerArity in Gen.Choose(2, 3)
            from innerTypeIndices in Gen.ArrayOf(Gen.Choose(0, ElementTypes.Length - 1), innerArity)
            from innerInvalidMask in GenInvalidMask(innerArity)
            from outerExtraTypeIdx in Gen.Choose(0, ElementTypes.Length - 1)
            from outerExtraIsInvalid in Gen.Elements(true, false)
            let innerElements = innerTypeIndices.Select((ti, i) => (
                Type: ElementTypes[ti].Type,
                Value: ElementTypes[ti].ValidLiteral,
                PatternLiteral: innerInvalidMask[i]
                    ? ElementTypes[ti].IncompatibleLiteral
                    : ElementTypes[ti].ValidLiteral
            )).ToArray()
            let outerExtraType = ElementTypes[outerExtraTypeIdx].Type
            let outerExtraValue = ElementTypes[outerExtraTypeIdx].ValidLiteral
            let outerExtraPattern = outerExtraIsInvalid
                ? ElementTypes[outerExtraTypeIdx].IncompatibleLiteral
                : ElementTypes[outerExtraTypeIdx].ValidLiteral
            let innerType = $"({string.Join(", ", innerElements.Select(e => e.Type))})"
            let innerValue = $"({string.Join(", ", innerElements.Select(e => e.Value))})"
            let innerPattern = $"({string.Join(", ", innerElements.Select(e => e.PatternLiteral))})"
            let tupleType = $"({innerType}, {outerExtraType})"
            let tupleValue = $"({innerValue}, {outerExtraValue})"
            let pattern = $"({innerPattern}, {outerExtraPattern})"
            let invalidCount = innerInvalidMask.Count(m => m) + (outerExtraIsInvalid ? 1 : 0)
            select new NestedElementValidationScenario(tupleType, tupleValue, pattern, invalidCount);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a boolean mask of the given length with at least one true value.
    /// True positions represent elements that will have incompatible patterns.
    /// </summary>
    private static Gen<bool[]> GenInvalidMask(int length)
    {
        return Gen.ArrayOf(Gen.Elements(true, false), length)
            .Where(mask => mask.Any(m => m));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Recursive element type validation
    //
    // **Validates: Requirements 4.3, 5.2**
    //
    // For any tuple type with arity N and a tuple positional pattern with N
    // elements, the type checker SHALL validate each element subpattern against
    // the corresponding element type of the tuple, emitting a diagnostic for
    // each type-incompatible element.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TuplePositionalPatternElementValidationPropertyTests) })]
    public Property FlatTuple_InvalidElements_EmitDiagnosticsForEachInvalidElement(
        FlatElementValidationScenario scenario)
    {
        var source = $$"""
            class Program {
                public static void Main() {
                    {{scenario.TupleType}} t = {{scenario.TupleValue}};
                    if (t is {{scenario.Pattern}}) { }
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Each invalid element should produce a MIST_TC_0051 (constant type mismatch) diagnostic
        var mismatchDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0051")
            .ToList();

        var hasExpectedCount = mismatchDiagnostics.Count >= scenario.InvalidElementCount;

        return hasExpectedCount
            .ToProperty()
            .Label($"Expected at least {scenario.InvalidElementCount} MIST_TC_0051 diagnostics " +
                   $"but got {mismatchDiagnostics.Count}. " +
                   $"All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]. " +
                   $"Source:\n{source}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TuplePositionalPatternElementValidationPropertyTests) })]
    public Property NestedTuple_InvalidElements_EmitDiagnosticsRecursively(
        NestedElementValidationScenario scenario)
    {
        var source = $$"""
            class Program {
                public static void Main() {
                    {{scenario.TupleType}} t = {{scenario.TupleValue}};
                    if (t is {{scenario.Pattern}}) { }
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Each invalid element (including those in nested tuples) should produce
        // a MIST_TC_0051 (constant type mismatch) diagnostic
        var mismatchDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0051")
            .ToList();

        var hasExpectedCount = mismatchDiagnostics.Count >= scenario.InvalidElementCount;

        return hasExpectedCount
            .ToProperty()
            .Label($"Expected at least {scenario.InvalidElementCount} MIST_TC_0051 diagnostics " +
                   $"but got {mismatchDiagnostics.Count}. " +
                   $"All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]. " +
                   $"Source:\n{source}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TuplePositionalPatternElementValidationPropertyTests) })]
    public Property FlatTuple_AllValidElements_NoTypeMismatchDiagnostics(
        FlatElementValidationScenario scenario)
    {
        // Build a scenario where ALL elements are valid (use valid literals for all)
        var arity = scenario.TupleType.Count(c => c == ',') + 1;
        var typeIndices = Enumerable.Range(0, arity)
            .Select(_ => 0) // use int for all
            .ToArray();

        var tupleType = $"({string.Join(", ", typeIndices.Select(ti => ElementTypes[ti].Type))})";
        var tupleValue = $"({string.Join(", ", typeIndices.Select(ti => ElementTypes[ti].ValidLiteral))})";
        var pattern = $"({string.Join(", ", typeIndices.Select(ti => ElementTypes[ti].ValidLiteral))})";

        var source = $$"""
            class Program {
                public static void Main() {
                    {{tupleType}} t = {{tupleValue}};
                    if (t is {{pattern}}) { }
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No MIST_TC_0051 diagnostics should be emitted when all elements are type-compatible
        var mismatchDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0051")
            .ToList();

        var noMismatch = mismatchDiagnostics.Count == 0;

        return noMismatch
            .ToProperty()
            .Label($"Expected no MIST_TC_0051 diagnostics for all-valid pattern " +
                   $"but got {mismatchDiagnostics.Count}: " +
                   $"[{string.Join("; ", mismatchDiagnostics.Select(d => d.Message))}]. " +
                   $"Source:\n{source}");
    }
}
