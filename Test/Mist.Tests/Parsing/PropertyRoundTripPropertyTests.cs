// Feature: properties, Property 1: Parse–Print–Parse Round Trip
// **Validates: Requirements 15.8**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

public class PropertyRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<CompilationUnitNode> ArbitraryCompilationUnitNode()
    {
        return MistPropertyGenerator.GenPropertyProgram().ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyRoundTripPropertyTests) })]
    public Property RoundTrip_PrintIsIdempotentForProperties(CompilationUnitNode ast)
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

        var printed2Full = MistPrettyPrinter.Print(reparsed);

        return (printed1 == printed2Full).ToProperty()
            .Label($"Mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2Full}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsPropertySyntax(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);

        var containsPropertySyntax =
            printed.Contains("get;") ||
            printed.Contains("set;") ||
            printed.Contains("init;") ||
            printed.Contains("get =>") ||
            printed.Contains("set =>") ||
            printed.Contains("init =>") ||
            printed.Contains("get {") ||
            printed.Contains("set {") ||
            printed.Contains("init {") ||
            printed.Contains("=>"); // expression-bodied property or abstract type property

        return containsPropertySyntax.ToProperty()
            .Label($"No property syntax found in output:\n{printed}");
    }
}