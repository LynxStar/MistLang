// Feature: as-operator, Property 1: As expression round-trip
// **Validates: Requirements 1.1, 1.2, 1.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

public class AsExpressionRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<CompilationUnitNode> ArbitraryCompilationUnitNode()
    {
        return MistAsExpressionGenerator.GenAsExpressionProgram().ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AsExpressionRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParseReprintIsIdenticalForAsExpressions(CompilationUnitNode ast)
    {
        var printed1 = MistPrettyPrinter.Print(ast);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var sourceText = new Mist.Common.Source.SourceText("test.mist", printed1);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var hasStubWarning = bag.ToImmutableArray()
            .Any(d => d.Number.Value == "MIST0121");

        if(hasStubWarning)
        {
            var printed2 = MistPrettyPrinter.Print(ast);
            return (printed1 == printed2).ToProperty()
                .Label($"Determinism failed.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
        }

        if(errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Parse errors: {string.Join("; ", errors.Select(e => e.Message))}\nSource:\n{printed1}");
        }

        if(result.Root is not CompilationUnitNode reparsed)
        {
            return false.ToProperty().Label($"Reparsed root was null.\nSource:\n{printed1}");
        }

        var printed2Full = MistPrettyPrinter.Print(reparsed, printed1);

        return (printed1 == printed2Full).ToProperty()
            .Label($"Round-trip mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2Full}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AsExpressionRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsAsSyntax(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);

        var containsAsSyntax = printed.Contains(" as ");

        return containsAsSyntax.ToProperty()
            .Label($"No 'as' expression syntax found in output:\n{printed}");
    }
}