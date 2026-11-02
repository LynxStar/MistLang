using Mist.Generated;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

[Trait("Tier", "2")]
public sealed class ResultPostfixParsingTests
{
    [Fact]
    public void PropagationPrecedesFollowingOptionalAccess()
    {
        var postfix = Parse((TokenKind.IDENTIFIER, "ReadPerson"), (TokenKind.LEFT_PARENTHESIS, "("),
            (TokenKind.RIGHT_PARENTHESIS, ")"), (TokenKind.EXCLAMATION, "!"), (TokenKind.QUESTION_DOT, "?."), (TokenKind.IDENTIFIER, "Name"));
        Assert.Collection(postfix.Suffix,
            suffix => Assert.IsType<InvocationSuffixPostfixSuffixNode>(suffix),
            suffix => Assert.IsType<PropagationSuffixPostfixSuffixNode>(suffix),
            suffix => Assert.IsType<OptionalMemberAccessSuffixPostfixSuffixNode>(suffix));
    }

    [Fact]
    public void GroupingMakesTheWholeCompoundThePropagationOperand()
    {
        var postfix = Parse((TokenKind.LEFT_PARENTHESIS, "("), (TokenKind.IDENTIFIER, "x"), (TokenKind.PLUS, "+"),
            (TokenKind.IDENTIFIER, "y"), (TokenKind.RIGHT_PARENTHESIS, ")"), (TokenKind.EXCLAMATION, "!"));
        _ = Assert.IsType<ParenthesizedExpressionPrimaryNode>(postfix.Target);
        _ = Assert.IsType<PropagationSuffixPostfixSuffixNode>(Assert.Single(postfix.Suffix));
    }

    [Fact]
    public void PrefixNegationAndPostfixPropagationHaveDistinctNodes()
    {
        var (tokens, diagnostics) = Tokens((TokenKind.EXCLAMATION, "!"), (TokenKind.IDENTIFIER, "flag"), (TokenKind.EXCLAMATION, "!"));
        var unary = Assert.IsType<LogicalNotUnaryNode>(MistParser.ParseUnary(tokens, diagnostics));
        var direct = Assert.IsType<PostfixExpressionUnaryNode>(unary.Operand);
        _ = Assert.IsType<PropagationSuffixPostfixSuffixNode>(Assert.Single(direct.Primary.Suffix));
        Assert.Empty(diagnostics.Entries);
    }

    private static PostfixNode Parse(params (TokenKind Kind, string Text)[] parts)
    {
        var (tokens, diagnostics) = Tokens(parts);
        var postfix = Assert.IsType<PostfixNode>(MistParser.ParsePostfix(tokens, diagnostics));
        Assert.Empty(diagnostics.Entries);
        Assert.Equal(parts.Length, MistParser.LastPosition);
        return postfix;
    }

    private static (TokenStream, DiagnosticBag) Tokens(params (TokenKind Kind, string Text)[] parts)
    {
        var source = string.Join(" ", parts.Select(part => part.Text));
        var offset = 0;
        var tokens = new List<Token>();
        foreach(var (kind, text) in parts)
        {
            tokens.Add(new Token((int)kind, new SourceSpan(offset, text.Length), new LinePosition(0, offset),
                new LinePosition(0, offset + text.Length), TokenChannel.Default, text));
            offset += text.Length + 1;
        }

        tokens.Add(new Token((int)TokenKind.EndOfFile, new SourceSpan(source.Length, 0), default, default, TokenChannel.Default, ""));
        return (new TokenStream(new SourceText("postfix.mist", source), [.. tokens]), new DiagnosticBag());
    }
}