// Feature: tuple-pattern-matching, Property 1: Pretty-print round-trip
// **Validates: Requirements 1.1, 1.3, 1.4, 5.1, 6.1, 6.2, 6.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

public class TuplePositionalPatternRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<TuplePositionalPatternSourceScenario> ArbitraryTuplePositionalPatternSourceScenario()
    {
        return MistTuplePositionalPatternGenerator.GenTuplePositionalPatternSource().ToArbitrary();
    }

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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TuplePositionalPatternRoundTripPropertyTests) })]
    public Property RoundTrip_TuplePositionalPattern_PrintParseReprintIsIdentical(
        TuplePositionalPatternSourceScenario scenario)
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TuplePositionalPatternRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsTuplePositionalPatternSyntax(
        TuplePositionalPatternSourceScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        var containsIsExpression = printed1.Contains(" is (");

        return containsIsExpression.ToProperty()
            .Label($"No tuple positional pattern syntax found in output:\n{printed1}\nScenario: {scenario.Description}");
    }
}