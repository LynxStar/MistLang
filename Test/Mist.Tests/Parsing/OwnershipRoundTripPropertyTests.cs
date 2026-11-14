// Feature: ownership-system, Property 1: Ownership-annotated round-trip parsing
// **Validates: Requirements 20.1, 20.2, 20.3, 2.1, 2.2, 2.3, 2.4, 2.5**

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

public class OwnershipRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    public static Arbitrary<OwnershipSourceScenario> ArbitraryOwnershipSourceScenario()
    {
        return MistOwnershipGenerator.GenOwnershipSource().ToArbitrary();
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OwnershipRoundTripPropertyTests) })]
    public Property RoundTrip_OwnershipAnnotatedDeclaration_PrintParseIsIdempotent(OwnershipSourceScenario scenario)
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
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OwnershipRoundTripPropertyTests) })]
    public Property RoundTrip_OwnershipSpecifiersPreserved(OwnershipSourceScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var containsOwnership = printed1.Contains("owned ") ||
                                printed1.Contains("borrowed ") ||
                                printed1.Contains("readonly ref ") ||
                                printed1.Contains("frozen ");

        return containsOwnership.ToProperty()
            .Label($"No ownership specifier found in output.\nSource: {scenario.Source}\nPrinted:\n{printed1}");
    }
}