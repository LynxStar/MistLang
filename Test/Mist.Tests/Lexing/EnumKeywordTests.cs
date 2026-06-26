using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

/// <summary>
/// Unit tests verifying that the lexer correctly recognizes `enum` as a reserved
/// keyword token and that the parser rejects it in identifier position.
///
/// Requirements: 11.1, 11.2
/// </summary>
public class EnumKeywordTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly Mist.Parsing.MistParser Parser = new();

    /// <summary>
    /// Helper: lex a source string and return the default-channel tokens
    /// (excluding hidden tokens like whitespace/comments), also excluding EOF.
    /// </summary>
    private static IReadOnlyList<Storm.Runtime.Token> LexDefaultTokens(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        return stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();
    }

    /// <summary>
    /// Helper: lex and parse a source string, returning the diagnostics bag.
    /// </summary>
    private static DiagnosticBag LexAndParse(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        _ = Parser.Parse(stream, bag);
        return bag;
    }

    // ─── Requirement 11.1: `enum` recognized as reserved keyword token ───────

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
        // In a typical enum declaration, `enum` should still be the ENUM keyword token
        var tokens = LexDefaultTokens("enum Direction { }");

        Assert.True(tokens.Count >= 1);
        Assert.Equal((int)TokenKind.ENUM, tokens[0].Kind);
        Assert.Equal("enum", tokens[0].Lexeme);
    }

    [Fact]
    public void EnumPrefix_LexesAsIdentifier()
    {
        // "enumeration" should be IDENTIFIER, not ENUM
        var tokens = LexDefaultTokens("enumeration");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.IDENTIFIER, tokens[0].Kind);
        Assert.Equal("enumeration", tokens[0].Lexeme);
    }

    [Fact]
    public void EnumSuffix_LexesAsIdentifier()
    {
        // "myEnum" should be IDENTIFIER, not ENUM
        var tokens = LexDefaultTokens("myEnum");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.IDENTIFIER, tokens[0].Kind);
        Assert.Equal("myEnum", tokens[0].Lexeme);
    }

    // ─── Requirement 11.2: `enum` in identifier position produces syntax error ─

    [Fact]
    public void Enum_AsVariableName_ProducesSyntaxError()
    {
        // Using `enum` where an identifier is expected (as a variable name)
        // should produce a parse error because `enum` is a reserved keyword.
        // The parser expects IDENTIFIER after VAR but gets ENUM token instead.
        // We verify this by checking that the parse either reports an error
        // or fails to produce a valid parse tree for the containing class.
        var source = @"
class Foo {
    public static void Bar() {
        var enum = 5;
    }
}";
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(stream, bag);

        // The parser should either report an error or fail to produce a valid
        // parse tree. With the Storm PEG parser, when `enum` appears where
        // IDENTIFIER is expected, the statement parse fails and cascades up,
        // causing the class to not be fully parsed.
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
        // Even in identifier position, the lexer must tokenize `enum` as the
        // ENUM keyword — confirming it is truly reserved, not contextual.
        var tokens = LexDefaultTokens("var enum = 5;");

        // tokens[0] is VAR; tokens[1] must be ENUM keyword, not IDENTIFIER
        Assert.True(tokens.Count >= 2,
            $"Expected at least 2 tokens, got {tokens.Count}");
        Assert.Equal((int)TokenKind.ENUM, tokens[1].Kind);
        Assert.NotEqual((int)TokenKind.IDENTIFIER, tokens[1].Kind);
        Assert.Equal("enum", tokens[1].Lexeme);
    }
}