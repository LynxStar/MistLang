// Feature: ownership-system, Property 1: Ownership-annotated round-trip parsing
// **Validates: Requirements 20.1, 20.2, 20.3, 2.1, 2.2, 2.3, 2.4, 2.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for ownership-annotated round-trip parsing.
///
/// **Validates: Requirements 20.1, 20.2, 20.3, 2.1, 2.2, 2.3, 2.4, 2.5**
///
/// For any valid Mist AST containing ownership specifiers (owned, borrowed, readonly ref, frozen)
/// on variables, parameters, return types, and type declarations, pretty-printing the AST and
/// then parsing the result and pretty-printing again SHALL produce identical output.
/// Compound specifiers (readonly ref) SHALL be preserved through the cycle.
/// </summary>
public class OwnershipRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<OwnershipSourceScenario> ArbitraryOwnershipSourceScenario() =>
        MistOwnershipGenerator.GenOwnershipSource().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Ownership-annotated round-trip parsing
    //
    // **Validates: Requirements 20.1, 20.2, 20.3, 2.1, 2.2, 2.3, 2.4, 2.5**
    //
    // For any valid ownership-annotated declaration source string, parsing and
    // pretty-printing the result, then parsing and pretty-printing again, SHALL
    // produce identical output (idempotence / round-trip stability).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OwnershipRoundTripPropertyTests) })]
    public Property RoundTrip_OwnershipAnnotatedDeclaration_PrintParseIsIdempotent(OwnershipSourceScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if (string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OwnershipRoundTripPropertyTests) })]
    public Property RoundTrip_OwnershipSpecifiersPreserved(OwnershipSourceScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if (string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Verify that ownership specifiers are preserved in the printed output.
        // At least one ownership keyword should appear in the output.
        var containsOwnership = printed1.Contains("owned ") ||
                                printed1.Contains("borrowed ") ||
                                printed1.Contains("readonly ref ") ||
                                printed1.Contains("frozen ");

        return containsOwnership.ToProperty()
            .Label($"No ownership specifier found in output.\nSource: {scenario.Source}\nPrinted:\n{printed1}");
    }
}
