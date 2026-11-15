using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

public class OptionalOperatorTokenPriorityTests
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

    [Fact]
    public void QuestionQuestionEqual_LexesAsSingleToken()
    {
        var tokens = LexDefaultTokens("??=");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.QUESTION_QUESTION_EQUAL, tokens[0].Kind);
        Assert.Equal("??=", tokens[0].Lexeme);
    }

    [Fact]
    public void QuestionQuestion_LexesAsSingleToken()
    {
        var tokens = LexDefaultTokens("??");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.QUESTION_QUESTION, tokens[0].Kind);
        Assert.Equal("??", tokens[0].Lexeme);
    }

    [Fact]
    public void QuestionDot_LexesAsSingleToken()
    {
        var tokens = LexDefaultTokens("?.");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.QUESTION_DOT, tokens[0].Kind);
        Assert.Equal("?.", tokens[0].Lexeme);
    }

    [Fact]
    public void SingleQuestion_LexesAsSingleToken()
    {
        var tokens = LexDefaultTokens("?");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.QUESTION, tokens[0].Kind);
        Assert.Equal("?", tokens[0].Lexeme);
    }

    [Fact]
    public void QuestionQuestionDot_LexesAsQuestionQuestion_Plus_Dot()
    {
        // "??." should be QUESTION_QUESTION + DOT, not QUESTION + QUESTION_DOT
        var tokens = LexDefaultTokens("??.");

        Assert.Equal(2, tokens.Count);
        Assert.Equal((int)TokenKind.QUESTION_QUESTION, tokens[0].Kind);
        Assert.Equal("??", tokens[0].Lexeme);
        Assert.Equal((int)TokenKind.DOT, tokens[1].Kind);
        Assert.Equal(".", tokens[1].Lexeme);
    }

    [Fact]
    public void QuestionQuestionEqualFollowedByIdentifier_LexesCorrectly()
    {
        var tokens = LexDefaultTokens("??=x");

        Assert.Equal(2, tokens.Count);
        Assert.Equal((int)TokenKind.QUESTION_QUESTION_EQUAL, tokens[0].Kind);
        Assert.Equal("??=", tokens[0].Lexeme);
        Assert.Equal((int)TokenKind.IDENTIFIER, tokens[1].Kind);
        Assert.Equal("x", tokens[1].Lexeme);
    }
}