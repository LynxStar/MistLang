using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

public class PipeTokenTests
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
    public void SinglePipe_ProducesPipeToken()
    {
        var tokens = LexDefaultTokens("|");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.PIPE, tokens[0].Kind);
        Assert.Equal("|", tokens[0].Lexeme);
    }

    [Fact]
    public void DoublePipe_ProducesLogicalOrToken()
    {
        var tokens = LexDefaultTokens("||");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.LOGICAL_OR, tokens[0].Kind);
        Assert.Equal("||", tokens[0].Lexeme);
    }

    [Fact]
    public void PipeSpacePipe_ProducesTwoPipeTokens()
    {
        var tokens = LexDefaultTokens("| |");

        Assert.Equal(2, tokens.Count);
        Assert.Equal((int)TokenKind.PIPE, tokens[0].Kind);
        Assert.Equal("|", tokens[0].Lexeme);
        Assert.Equal((int)TokenKind.PIPE, tokens[1].Kind);
        Assert.Equal("|", tokens[1].Lexeme);
    }
}