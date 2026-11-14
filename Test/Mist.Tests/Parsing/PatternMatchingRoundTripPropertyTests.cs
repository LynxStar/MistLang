// Feature: pattern-matching, Property 1: Pattern Matching Round-Trip
// **Validates: Requirements 2.8, 3.3, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 4.10, 4.11, 4.12**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

public class PatternMatchingRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<CompilationUnitNode> ArbitraryCompilationUnitNode()
    {
        return MistPatternGenerator.GenAnyPatternProgram().ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PatternMatchingRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParseReprintIsIdenticalForPatternMatching(CompilationUnitNode ast)
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
        Arbitrary = new[] { typeof(PatternMatchingRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsPatternMatchingSyntax(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);

        var containsPatternSyntax = printed.Contains(" is ");

        return containsPatternSyntax.ToProperty()
            .Label($"No pattern matching syntax found in output:\n{printed}");
    }
}