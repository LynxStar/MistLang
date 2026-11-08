using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;

namespace Mist.Tests.Lexing;

[Trait("Tier", "1")]
public sealed class CompoundAssignmentTokenTests
{
    [Theory]
    [InlineData("+=", TokenKind.PLUS_EQUAL)]
    [InlineData("-=", TokenKind.MINUS_EQUAL)]
    [InlineData("*=", TokenKind.STAR_EQUAL)]
    [InlineData("/=", TokenKind.SLASH_EQUAL)]
    [InlineData("%=", TokenKind.PERCENT_EQUAL)]
    [InlineData("++", TokenKind.PLUS_PLUS)]
    [InlineData("--", TokenKind.MINUS_MINUS)]
    public void EachSpellingIsOneToken(string spelling, TokenKind kind)
    {
        Assert.Equal([TokenKind.IDENTIFIER, kind, TokenKind.INTEGER_LITERAL], Kinds($"x{spelling}1"));
    }

    [Fact]
    public void SeparatedSignsStayTwoOperators()
    {
        Assert.Equal([TokenKind.MINUS, TokenKind.MINUS, TokenKind.IDENTIFIER], Kinds("- -x"));
        Assert.Equal([TokenKind.IDENTIFIER, TokenKind.PLUS, TokenKind.EQUAL, TokenKind.INTEGER_LITERAL], Kinds("x + = 1"));
    }

    private static TokenKind[] Kinds(string source)
    {
        var stream = new Mist.Lexing.MistLexer().Lex(new SourceText("tokens.mist", source), new DiagnosticBag());
        return [.. stream.DefaultChannelTokens.Where(token => token.Kind != (int)TokenKind.EndOfFile).Select(token => (TokenKind)token.Kind)];
    }
}