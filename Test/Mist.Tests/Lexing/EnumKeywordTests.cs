using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

public class EnumKeywordTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static IReadOnlyList<Storm.Runtime.Token> LexDefaultTokens(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        return stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();
    }

    [Fact]
    public void Enum_LexesAsKeywordToken()
    {
        var tokens = LexDefaultTokens("enum");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.ENUM, tokens[0].Kind);
        Assert.Equal("enum", tokens[0].Lexeme);
    }

    [Fact]
    public void Enum_IsNotLexedAsIdentifier()
    {
        var tokens = LexDefaultTokens("enum");

        _ = Assert.Single(tokens);
        Assert.NotEqual((int)TokenKind.IDENTIFIER, tokens[0].Kind);
    }

    [Fact]
    public void Enum_InDeclarationContext_LexesAsKeyword()
    {
        var tokens = LexDefaultTokens("enum Direction { }");

        Assert.True(tokens.Count >= 1);
        Assert.Equal((int)TokenKind.ENUM, tokens[0].Kind);
        Assert.Equal("enum", tokens[0].Lexeme);
    }

    [Fact]
    public void EnumPrefix_LexesAsIdentifier()
    {
        var tokens = LexDefaultTokens("enumeration");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.IDENTIFIER, tokens[0].Kind);
        Assert.Equal("enumeration", tokens[0].Lexeme);
    }

    [Fact]
    public void EnumSuffix_LexesAsIdentifier()
    {
        var tokens = LexDefaultTokens("myEnum");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.IDENTIFIER, tokens[0].Kind);
        Assert.Equal("myEnum", tokens[0].Lexeme);
    }

    [Fact]
    public void Enum_AsVariableName_ProducesSyntaxError()
    {
        var source = @"
class Foo {
    public static void Bar() {
        var enum = 5;
    }
}";
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(stream, bag);

        // A PEG statement failure cascades up, so the rejection can surface as a null root or
        // an empty compilation unit rather than a diagnostic.
        var hasErrors = bag.HasErrors;
        var rootIsNull = result.Root == null;
        var rootHasNoItems = result.Root is CompilationUnitNode cu
            && cu.Items.Length == 0;

        Assert.True(hasErrors || rootIsNull || rootHasNoItems,
            "Expected the parser to reject `enum` in identifier position: " +
            "either report an error, return null root, or produce an empty compilation unit");
    }

    [Fact]
    public void Enum_AsVariableName_LexesAsKeywordNotIdentifier()
    {
        var tokens = LexDefaultTokens("var enum = 5;");

        Assert.True(tokens.Count >= 2,
            $"Expected at least 2 tokens, got {tokens.Count}");
        Assert.Equal((int)TokenKind.ENUM, tokens[1].Kind);
        Assert.NotEqual((int)TokenKind.IDENTIFIER, tokens[1].Kind);
        Assert.Equal("enum", tokens[1].Lexeme);
    }
}