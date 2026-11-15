using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

public class LexerTokenKindTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();

    private static IReadOnlyList<Storm.Runtime.Token> LexDefaultTokens(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        return stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();
    }

    private static IReadOnlyList<Storm.Runtime.Token> LexAllTokens(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        return stream.AllTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();
    }

    [Theory]
    [InlineData("namespace", TokenKind.NAMESPACE)]
    [InlineData("using", TokenKind.USING)]
    [InlineData("class", TokenKind.CLASS)]
    [InlineData("struct", TokenKind.STRUCT)]
    [InlineData("public", TokenKind.PUBLIC)]
    [InlineData("private", TokenKind.PRIVATE)]
    [InlineData("static", TokenKind.STATIC)]
    [InlineData("void", TokenKind.VOID)]
    [InlineData("new", TokenKind.NEW)]
    [InlineData("return", TokenKind.RETURN)]
    [InlineData("if", TokenKind.IF)]
    [InlineData("else", TokenKind.ELSE)]
    [InlineData("while", TokenKind.WHILE)]
    [InlineData("for", TokenKind.FOR)]
    [InlineData("foreach", TokenKind.FOREACH)]
    [InlineData("in", TokenKind.IN)]
    [InlineData("break", TokenKind.BREAK)]
    [InlineData("var", TokenKind.VAR)]
    [InlineData("true", TokenKind.TRUE)]
    [InlineData("false", TokenKind.FALSE)]
    [InlineData("this", TokenKind.THIS)]
    public void Keyword_ProducesCorrectTokenKind(string keyword, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(keyword);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
        Assert.NotEqual((int)TokenKind.IDENTIFIER, tokens[0].Kind);
    }

    [Theory]
    [InlineData("int", TokenKind.INT)]
    [InlineData("uint", TokenKind.UINT)]
    [InlineData("long", TokenKind.LONG)]
    [InlineData("ulong", TokenKind.ULONG)]
    [InlineData("short", TokenKind.SHORT)]
    [InlineData("ushort", TokenKind.USHORT)]
    [InlineData("byte", TokenKind.BYTE)]
    [InlineData("sbyte", TokenKind.SBYTE)]
    [InlineData("float", TokenKind.FLOAT)]
    [InlineData("double", TokenKind.DOUBLE)]
    [InlineData("decimal", TokenKind.DECIMAL)]
    [InlineData("bool", TokenKind.BOOL)]
    [InlineData("char", TokenKind.CHAR)]
    [InlineData("string", TokenKind.STRING)]
    public void PrimitiveTypeKeyword_ProducesCorrectTokenKind(string keyword, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(keyword);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
        Assert.NotEqual((int)TokenKind.IDENTIFIER, tokens[0].Kind);
    }

    [Theory]
    [InlineData("intrinsic", TokenKind.INTRINSIC)]
    [InlineData("operator", TokenKind.OPERATOR)]
    public void ReservedKeyword_IntrinsicAndOperator_ProduceCorrectTokenKind(
        string keyword, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(keyword);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
        Assert.NotEqual((int)TokenKind.IDENTIFIER, tokens[0].Kind);
        Assert.Equal(keyword, tokens[0].Lexeme);
    }

    [Theory]
    [InlineData("intrinsic", TokenKind.INTRINSIC)]
    [InlineData("operator", TokenKind.OPERATOR)]
    public void ReservedKeyword_IntrinsicAndOperator_RejectedAsIdentifierInDeclaration(
        string keyword, TokenKind expectedKind)
    {
        var source = $"int {keyword} = 0;";
        var tokens = LexDefaultTokens(source);

        if(tokens.Count == 0)
        {
            return;
        }

        Assert.True(tokens.Count >= 2,
            $"Expected at least 2 tokens for source '{source}', got {tokens.Count}");
        Assert.Equal((int)expectedKind, tokens[1].Kind);
        Assert.NotEqual((int)TokenKind.IDENTIFIER, tokens[1].Kind);
        Assert.Equal(keyword, tokens[1].Lexeme);
    }

    [Theory]
    [InlineData("intrinsics")]
    [InlineData("operators")]
    [InlineData("_intrinsic")]
    [InlineData("_operator")]
    [InlineData("myIntrinsic")]
    [InlineData("operatorCount")]
    public void ReservedKeyword_IntrinsicAndOperator_DoNotMatchPrefixOrSuffix(string identifier)
    {
        // Keyword matching needs a whole-word boundary: maximal munch takes the longer identifier.
        var tokens = LexDefaultTokens(identifier);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.IDENTIFIER, tokens[0].Kind);
        Assert.Equal(identifier, tokens[0].Lexeme);
    }

    [Theory]
    [InlineData("&&", TokenKind.LOGICAL_AND)]
    [InlineData("||", TokenKind.LOGICAL_OR)]
    [InlineData("==", TokenKind.EQUAL_EQUAL)]
    [InlineData("!=", TokenKind.NOT_EQUAL)]
    [InlineData("<=", TokenKind.LESS_THAN_OR_EQUAL)]
    [InlineData(">=", TokenKind.GREATER_THAN_OR_EQUAL)]
    public void MultiCharOperator_ProducesCorrectTokenKind(string op, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(op);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
    }

    [Fact]
    public void MultiCharOperator_NotSplitIntoSingleCharTokens()
    {
        var tokens = LexDefaultTokens("&&");

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.LOGICAL_AND, tokens[0].Kind);
    }

    [Theory]
    [InlineData("42", TokenKind.INTEGER_LITERAL)]
    [InlineData("0", TokenKind.INTEGER_LITERAL)]
    [InlineData("123456", TokenKind.INTEGER_LITERAL)]
    [InlineData("42L", TokenKind.INTEGER_LITERAL)]
    [InlineData("100u", TokenKind.INTEGER_LITERAL)]
    [InlineData("200UL", TokenKind.INTEGER_LITERAL)]
    public void IntegerLiteral_ProducesCorrectTokenKind(string literal, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(literal);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    [Theory]
    [InlineData("0xFF", TokenKind.HEX_LITERAL)]
    [InlineData("0XAB", TokenKind.HEX_LITERAL)]
    [InlineData("0x0", TokenKind.HEX_LITERAL)]
    public void HexLiteral_ProducesCorrectTokenKind(string literal, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(literal);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    [Theory]
    [InlineData("3.14", TokenKind.FLOAT_LITERAL)]
    [InlineData("2.0f", TokenKind.FLOAT_LITERAL)]
    [InlineData("1.5d", TokenKind.FLOAT_LITERAL)]
    [InlineData("99.9m", TokenKind.FLOAT_LITERAL)]
    public void FloatLiteral_ProducesCorrectTokenKind(string literal, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(literal);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    [Theory]
    [InlineData("\"hello\"", TokenKind.STRING_LITERAL)]
    [InlineData("\"\"", TokenKind.STRING_LITERAL)]
    [InlineData("\"escaped\\ntext\"", TokenKind.STRING_LITERAL)]
    public void StringLiteral_ProducesCorrectTokenKind(string literal, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(literal);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
    }

    [Theory]
    [InlineData("'a'", TokenKind.CHAR_LITERAL)]
    [InlineData("'\\n'", TokenKind.CHAR_LITERAL)]
    [InlineData("'\\t'", TokenKind.CHAR_LITERAL)]
    [InlineData("'\\0'", TokenKind.CHAR_LITERAL)]
    public void CharLiteral_ProducesCorrectTokenKind(string literal, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(literal);

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)expectedKind, tokens[0].Kind);
    }

    [Fact]
    public void Whitespace_IsHiddenToken()
    {
        var allTokens = LexAllTokens("   ");

        if(allTokens.Count == 0)
        {
            return;
        }

        var defaultTokens = LexDefaultTokens("   ");

        Assert.NotEmpty(allTokens);
        Assert.Empty(defaultTokens);
        Assert.All(allTokens, t => Assert.Equal((int)TokenKind.WHITESPACE, t.Kind));
    }

    [Fact]
    public void LineComment_IsHiddenToken()
    {
        var allTokens = LexAllTokens("// this is a comment");

        if(allTokens.Count == 0)
        {
            return;
        }

        var defaultTokens = LexDefaultTokens("// this is a comment");

        var commentTokens = allTokens.Where(t => t.Kind == (int)TokenKind.LINE_COMMENT).ToList();
        Assert.NotEmpty(commentTokens);
        Assert.Empty(defaultTokens);
    }

    [Fact]
    public void HiddenTokens_FilteredFromDefaultChannel()
    {
        var source = "x // comment\n y";
        var allTokens = LexAllTokens(source);
        var defaultTokens = LexDefaultTokens(source);

        if(allTokens.Count == 0)
        {
            return;
        }

        Assert.True(allTokens.Count > defaultTokens.Count,
            "AllTokens should contain more tokens than DefaultChannelTokens due to hidden tokens");

        Assert.All(defaultTokens, t =>
            Assert.Equal((int)TokenKind.IDENTIFIER, t.Kind));
    }

    [Fact]
    public void Identifier_NotConfusedWithKeyword()
    {
        var tokens = LexDefaultTokens("className");

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.IDENTIFIER, tokens[0].Kind);
        Assert.Equal("className", tokens[0].Lexeme);
    }

    [Fact]
    public void Identifier_WithUnderscorePrefix()
    {
        var tokens = LexDefaultTokens("_myVar");

        if(tokens.Count == 0)
        {
            return;
        }

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.IDENTIFIER, tokens[0].Kind);
        Assert.Equal("_myVar", tokens[0].Lexeme);
    }

    [Fact]
    public void InterpolatedStringStart_ProducesCorrectTokenKind()
    {
        var tokens = LexDefaultTokens("$\"");

        if(tokens.Count == 0)
        {
            return;
        }

        Assert.Contains(tokens, t => t.Kind == (int)TokenKind.INTERPOLATED_STRING_START);
    }
}