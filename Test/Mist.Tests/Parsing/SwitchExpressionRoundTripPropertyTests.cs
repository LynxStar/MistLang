// Feature: switch-expressions, Property 1: Parse–Print–Parse Round Trip
// **Validates: Requirements 11.6**

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
/// Property-based tests for switch expression parse–print–parse round trip.
///
/// **Validates: Requirements 11.6**
///
/// For any valid Mist source text containing switch expressions (with varying numbers of arms,
/// all pattern types, optional when guards, trailing commas, switch expressions in
/// assignment/argument/tuple contexts), parsing the source to an AST, pretty-printing the AST
/// back to source text, and parsing the result again SHALL produce an AST structurally equivalent
/// to the first parse.
/// </summary>
public class SwitchExpressionRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<SwitchExpressionSourceScenario> ArbitrarySwitchExpressionSourceScenario()
    {
        return MistSwitchExpressionGenerator.GenSwitchExpressionSource().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Parse–Print–Parse Round Trip
    //
    // **Validates: Requirements 11.6**
    //
    // For any valid Mist source with switch expressions, parsing the source to
    // an AST, pretty-printing the AST back to source text, and parsing the result
    // again SHALL produce a structurally equivalent AST (verified by comparing
    // their pretty-printed forms, since structurally equivalent ASTs produce
    // identical pretty-printed output).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SwitchExpressionRoundTripPropertyTests) })]
    public Property RoundTrip_SwitchExpression_ParsePrintParseIsIdempotent(SwitchExpressionSourceScenario scenario)
    {
        // Step 1: Parse the generated source → AST1
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify structural equivalence: printed1 == printed2
        // Structurally equivalent ASTs produce identical pretty-printed output.
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nScenario: {scenario.Description}\nSource:\n{scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SwitchExpressionRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsSwitchKeyword(SwitchExpressionSourceScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        // The output should contain the switch keyword
        var containsSwitchSyntax = printed1.Contains(" switch {");

        return containsSwitchSyntax.ToProperty()
            .Label($"No switch keyword found in output:\n{printed1}\nScenario: {scenario.Description}");
    }
}