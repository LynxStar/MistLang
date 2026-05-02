using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

/// <summary>
/// Unit tests for binary literal tokenization and digit separator support
/// across all numeric literal types.
///
/// Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9
/// </summary>
public class BinaryLiteralTokenTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();

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

    // ─── Binary Literal Token Kind (Req 1.2) ────────────────────────────────

    [Fact]
    public void BinaryLiteral_LexesAsBinaryLiteralToken_NotIntegerLiteral()
    {
        var tokens = LexDefaultTokens("0b1010");

        Assert.Single(tokens);
        Assert.Equal((int)TokenKind.BINARY_LITERAL, tokens[0].Kind);
        Assert.NotEqual((int)TokenKind.INTEGER_LITERAL, tokens[0].Kind);
        Assert.Equal("0b1010", tokens[0].Lexeme);
    }

    // ─── Binary Literal Prefixes (Req 1.1) ──────────────────────────────────

    [Theory]
    [InlineData("0b1010")]
    [InlineData("0B1010")]
    [InlineData("0b0")]
    [InlineData("0B0")]
    [InlineData("0b1")]
    [InlineData("0B1")]
    [InlineData("0b11111111")]
    [InlineData("0B00000000")]
    public void BinaryLiteral_WithBothPrefixes_ProducesBinaryLiteralToken(string literal)
    {
        var tokens = LexDefaultTokens(literal);

        Assert.Single(tokens);
        Assert.Equal((int)TokenKind.BINARY_LITERAL, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    // ─── Binary Literal Suffixes (Req 1.1) ──────────────────────────────────

    [Theory]
    [InlineData("0b1010L")]
    [InlineData("0b1010l")]
    [InlineData("0b1010u")]
    [InlineData("0b1010U")]
    [InlineData("0b1010UL")]
    [InlineData("0b1010ul")]
    [InlineData("0b1010Ul")]
    [InlineData("0b1010uL")]
    [InlineData("0b1010Lu")]
    [InlineData("0b1010lU")]
    [InlineData("0b1010LU")]
    [InlineData("0b1010lu")]
    public void BinaryLiteral_WithSuffix_ProducesSingleBinaryLiteralToken(string literal)
    {
        var tokens = LexDefaultTokens(literal);

        Assert.Single(tokens);
        Assert.Equal((int)TokenKind.BINARY_LITERAL, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    // ─── Binary Literal with Digit Separators (Req 1.3, 1.5) ────────────────

    [Theory]
    [InlineData("0b1111_0000")]
    [InlineData("0b1_0_1_0")]
    [InlineData("0B1111_0000_1111_0000")]
    [InlineData("0b1010_0101UL")]
    public void BinaryLiteral_WithDigitSeparators_ProducesSingleToken(string literal)
    {
        var tokens = LexDefaultTokens(literal);

        Assert.Single(tokens);
        Assert.Equal((int)TokenKind.BINARY_LITERAL, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    // ─── Integer Literal with Digit Separators (Req 8.1) ────────────────────

    [Theory]
    [InlineData("1_000_000")]
    [InlineData("1_0")]
    [InlineData("123_456_789")]
    [InlineData("1_000L")]
    [InlineData("1_000u")]
    public void IntegerLiteral_WithDigitSeparators_ProducesSingleToken(string literal)
    {
        var tokens = LexDefaultTokens(literal);

        Assert.Single(tokens);
        Assert.Equal((int)TokenKind.INTEGER_LITERAL, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    // ─── Hex Literal with Digit Separators (Req 8.2) ────────────────────────

    [Theory]
    [InlineData("0xFF_FF")]
    [InlineData("0xAB_CD_EF")]
    [InlineData("0XFF_FF")]
    [InlineData("0xFF_FFUL")]
    public void HexLiteral_WithDigitSeparators_ProducesSingleToken(string literal)
    {
        var tokens = LexDefaultTokens(literal);

        Assert.Single(tokens);
        Assert.Equal((int)TokenKind.HEX_LITERAL, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    // ─── Float Literal with Digit Separators (Req 8.3) ──────────────────────

    [Theory]
    [InlineData("1_000.50_25f")]
    [InlineData("1_000.0")]
    [InlineData("3_14.1_5d")]
    [InlineData("99_99.9_9m")]
    public void FloatLiteral_WithDigitSeparators_ProducesSingleToken(string literal)
    {
        var tokens = LexDefaultTokens(literal);

        Assert.Single(tokens);
        Assert.Equal((int)TokenKind.FLOAT_LITERAL, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Lexeme);
    }

    // ─── Invalid Separator Placement (Req 1.4, 8.4, 8.5, 8.6, 8.7, 8.8) ───
    // Invalid separator placement should NOT produce a single correct literal
    // token. The exact tokenization depends on the Storm lexer's behavior,
    // so we assert that the input does NOT lex as a single literal of the
    // expected type.

    [Fact]
    public void InvalidSeparator_BinaryPrefixUnderscore_DoesNotProduceSingleBinaryLiteral()
    {
        // 0b_1010 — underscore right after prefix is invalid (Req 1.4)
        var tokens = LexDefaultTokens("0b_1010");

        // Should NOT be a single BINARY_LITERAL token
        var isSingleBinaryLiteral = tokens.Count == 1
            && tokens[0].Kind == (int)TokenKind.BINARY_LITERAL;
        Assert.False(isSingleBinaryLiteral,
            "0b_1010 should not lex as a single BINARY_LITERAL token");
    }

    [Fact]
    public void InvalidSeparator_HexPrefixUnderscore_DoesNotProduceSingleHexLiteral()
    {
        // 0x_FF — underscore right after prefix is invalid (Req 8.4)
        var tokens = LexDefaultTokens("0x_FF");

        var isSingleHexLiteral = tokens.Count == 1
            && tokens[0].Kind == (int)TokenKind.HEX_LITERAL;
        Assert.False(isSingleHexLiteral,
            "0x_FF should not lex as a single HEX_LITERAL token");
    }

    [Fact]
    public void InvalidSeparator_ConsecutiveUnderscores_DoesNotProduceSingleIntegerLiteral()
    {
        // 1__000 — consecutive underscores are invalid (Req 8.8)
        var tokens = LexDefaultTokens("1__000");

        var isSingleIntegerLiteral = tokens.Count == 1
            && tokens[0].Kind == (int)TokenKind.INTEGER_LITERAL;
        Assert.False(isSingleIntegerLiteral,
            "1__000 should not lex as a single INTEGER_LITERAL token");
    }

    [Fact]
    public void InvalidSeparator_BeforeSuffix_DoesNotProduceSingleIntegerLiteral()
    {
        // 100_L — underscore before suffix is invalid (Req 8.5)
        var tokens = LexDefaultTokens("100_L");

        var isSingleIntegerLiteral = tokens.Count == 1
            && tokens[0].Kind == (int)TokenKind.INTEGER_LITERAL;
        Assert.False(isSingleIntegerLiteral,
            "100_L should not lex as a single INTEGER_LITERAL token");
    }

    [Fact]
    public void InvalidSeparator_BeforeDecimalPoint_DoesNotProduceSingleFloatLiteral()
    {
        // 1_.0 — underscore before decimal point is invalid (Req 8.6)
        var tokens = LexDefaultTokens("1_.0");

        var isSingleFloatLiteral = tokens.Count == 1
            && tokens[0].Kind == (int)TokenKind.FLOAT_LITERAL;
        Assert.False(isSingleFloatLiteral,
            "1_.0 should not lex as a single FLOAT_LITERAL token");
    }

    [Fact]
    public void InvalidSeparator_AfterDecimalPoint_DoesNotProduceSingleFloatLiteral()
    {
        // 1._0 — underscore after decimal point is invalid (Req 8.7)
        var tokens = LexDefaultTokens("1._0");

        var isSingleFloatLiteral = tokens.Count == 1
            && tokens[0].Kind == (int)TokenKind.FLOAT_LITERAL;
        Assert.False(isSingleFloatLiteral,
            "1._0 should not lex as a single FLOAT_LITERAL token");
    }

    // ─── Binary Prefix Alone (Req 1.4) ──────────────────────────────────────

    [Theory]
    [InlineData("0b")]
    [InlineData("0B")]
    public void BinaryPrefixAlone_DoesNotProduceBinaryLiteralToken(string input)
    {
        // 0b or 0B with no digits should not produce a BINARY_LITERAL token
        var tokens = LexDefaultTokens(input);

        var hasBinaryLiteral = tokens.Any(t => t.Kind == (int)TokenKind.BINARY_LITERAL);
        Assert.False(hasBinaryLiteral,
            $"'{input}' alone should not produce a BINARY_LITERAL token");
    }
}
