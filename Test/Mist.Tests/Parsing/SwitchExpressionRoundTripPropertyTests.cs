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
    // Structural equivalence of the two parses is checked through their printed forms:
    // equivalent ASTs print identically.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SwitchExpressionRoundTripPropertyTests) })]
    public Property RoundTrip_SwitchExpression_ParsePrintParseIsIdempotent(SwitchExpressionSourceScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var ast2 = ParseOrFail(printed1);

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nScenario: {scenario.Description}\nSource:\n{scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SwitchExpressionRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsSwitchKeyword(SwitchExpressionSourceScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        var containsSwitchSyntax = printed1.Contains(" switch {");

        return containsSwitchSyntax.ToProperty()
            .Label($"No switch keyword found in output:\n{printed1}\nScenario: {scenario.Description}");
    }
}