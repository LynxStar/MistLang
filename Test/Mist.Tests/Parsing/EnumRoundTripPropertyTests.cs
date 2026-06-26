// Feature: enums, Property 1: Enum declaration round-trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 12.1**

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
/// Property-based tests for enum declaration round-trip.
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 12.1**
///
/// For any valid enum declaration (with any combination of access modifiers, type annotations,
/// valued members, name-only members, and trailing commas), parsing the source then printing
/// the syntax tree back to source then parsing again SHALL produce an equivalent syntax tree.
/// </summary>
public class EnumRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<EnumSourceScenario> ArbitraryEnumSourceScenario() =>
        MistEnumGenerator.GenEnumSource().ToArbitrary();

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

        if(errors.Count > 0)
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Enum declaration round-trip
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 12.1**
    //
    // For any valid enum declaration source string, parsing and pretty-printing
    // the result, then parsing and pretty-printing again, SHALL produce identical
    // output (idempotence / round-trip stability).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(EnumRoundTripPropertyTests) })]
    public Property RoundTrip_EnumDeclaration_PrintParseIsIdempotent(EnumSourceScenario scenario)
    {
        // Step 1: Parse the generated source
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
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(EnumRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsEnumKeyword(EnumSourceScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        // The output should contain the enum keyword
        var containsEnumSyntax = printed1.Contains("enum ");

        return containsEnumSyntax.ToProperty()
            .Label($"No enum keyword found in output:\n{printed1}");
    }
}