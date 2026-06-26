// Feature: tuple-pattern-matching, Property 1: Pretty-print round-trip
// **Validates: Requirements 1.1, 1.3, 1.4, 5.1, 6.1, 6.2, 6.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for tuple positional pattern pretty-print round-trip.
///
/// **Validates: Requirements 1.1, 1.3, 1.4, 5.1, 6.1, 6.2, 6.3**
///
/// For any valid tuple positional pattern AST (with arbitrary subpatterns including
/// nested tuple patterns, relational, logical, constant, var, discard, and type patterns,
/// at any arity ≥ 2 and any nesting depth), pretty-printing the pattern to source text
/// and then parsing that source text back SHALL produce an AST that pretty-prints to
/// identical text.
/// </summary>
public class TuplePositionalPatternRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<TuplePositionalPatternSourceScenario> ArbitraryTuplePositionalPatternSourceScenario() =>
        MistTuplePositionalPatternGenerator.GenTuplePositionalPatternSource().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Pretty-print round-trip
    //
    // **Validates: Requirements 1.1, 1.3, 1.4, 5.1, 6.1, 6.2, 6.3**
    //
    // For any valid Mist source with tuple positional patterns, parsing the
    // source to an AST, pretty-printing the AST back to source text, and
    // parsing the result again SHALL produce a structurally equivalent AST
    // (verified by comparing their pretty-printed forms).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TuplePositionalPatternRoundTripPropertyTests) })]
    public Property RoundTrip_TuplePositionalPattern_PrintParseReprintIsIdentical(
        TuplePositionalPatternSourceScenario scenario)
    {
        // Step 1: Parse the generated source → AST1
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nScenario: {scenario.Description}\nSource:\n{scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TuplePositionalPatternRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsTuplePositionalPatternSyntax(
        TuplePositionalPatternSourceScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        // The output should contain tuple positional pattern syntax:
        // an 'is' keyword followed by a pattern containing '(' and ','
        var containsIsExpression = printed1.Contains(" is (");

        return containsIsExpression.ToProperty()
            .Label($"No tuple positional pattern syntax found in output:\n{printed1}\nScenario: {scenario.Description}");
    }
}