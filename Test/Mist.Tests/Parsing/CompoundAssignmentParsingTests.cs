using Mist.Generated;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class CompoundAssignmentParsingTests
{
    [Theory]
    [InlineData(TokenKind.PLUS_EQUAL, "+=")]
    [InlineData(TokenKind.MINUS_EQUAL, "-=")]
    [InlineData(TokenKind.STAR_EQUAL, "*=")]
    [InlineData(TokenKind.SLASH_EQUAL, "/=")]
    [InlineData(TokenKind.PERCENT_EQUAL, "%=")]
    public void ACompoundOperatorIsAnAssignmentOperator(TokenKind kind, string spelling)
    {
        var (tokens, diagnostics) = Tokens((TokenKind.IDENTIFIER, "x"), (kind, spelling), (TokenKind.INTEGER_LITERAL, "1"));

        var assignment = Assert.IsType<AssignmentNode>(MistParser.ParseAssignment(tokens, diagnostics));

        var group = Assert.NotNull(assignment.Group0);
        Assert.Equal((int)kind, group.Operator.Kind);
        Assert.Equal(spelling, group.Operator.Lexeme);
        _ = Assert.IsType<AssignmentFormExpressionNode>(group.Value);
        Assert.Empty(diagnostics.Entries);
        Assert.Equal(3, MistParser.LastPosition);
    }

    [Theory]
    [InlineData(TokenKind.PLUS_PLUS, "++")]
    [InlineData(TokenKind.MINUS_MINUS, "--")]
    public void APrefixIncrementIsAUnaryOperatorOverItsOperand(TokenKind kind, string spelling)
    {
        var (tokens, diagnostics) = Tokens((kind, spelling), (TokenKind.IDENTIFIER, "x"));

        var prefix = Assert.IsType<PrefixIncrementUnaryNode>(MistParser.ParseUnary(tokens, diagnostics));

        Assert.Equal((int)kind, prefix.Operator.Kind);
        var operand = Assert.IsType<PostfixExpressionUnaryNode>(prefix.Operand);
        _ = Assert.IsType<IdentifierExpressionPrimaryNode>(operand.Primary.Target);
        Assert.Empty(operand.Primary.Suffix);
        Assert.Empty(diagnostics.Entries);
    }

    [Theory]
    [InlineData(TokenKind.PLUS_PLUS, "++")]
    [InlineData(TokenKind.MINUS_MINUS, "--")]
    public void APostfixIncrementIsTheLastSuffixOfItsTarget(TokenKind kind, string spelling)
    {
        var (tokens, diagnostics) = Tokens((TokenKind.IDENTIFIER, "c"), (TokenKind.DOT, "."), (TokenKind.IDENTIFIER, "Count"), (kind, spelling));

        var postfix = Assert.IsType<PostfixNode>(MistParser.ParsePostfix(tokens, diagnostics));

        Assert.Collection(postfix.Suffix,
            suffix => Assert.IsType<MemberAccessSuffixPostfixSuffixNode>(suffix),
            suffix => Assert.Equal((int)kind, Assert.IsType<IncrementSuffixPostfixSuffixNode>(suffix).Operator.Kind));
        Assert.Empty(diagnostics.Entries);
    }

    [Fact]
    public void AnIncrementInsideAnIndexBelongsToTheIndexExpression()
    {
        var (tokens, diagnostics) = Tokens((TokenKind.IDENTIFIER, "items"), (TokenKind.LEFT_BRACKET, "["), (TokenKind.IDENTIFIER, "i"),
            (TokenKind.PLUS_PLUS, "++"), (TokenKind.RIGHT_BRACKET, "]"));

        var postfix = Assert.IsType<PostfixNode>(MistParser.ParsePostfix(tokens, diagnostics));

        var index = Assert.IsType<IndexSuffixPostfixSuffixNode>(Assert.Single(postfix.Suffix));
        var incremented = Assert.Single(Descendants(index.Index).OfType<PostfixNode>());
        Assert.Equal("i", Assert.IsType<IdentifierExpressionPrimaryNode>(incremented.Target).Name.Lexeme);
        _ = Assert.IsType<IncrementSuffixPostfixSuffixNode>(Assert.Single(incremented.Suffix));
        Assert.Empty(diagnostics.Entries);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
    {
        return node.Children().SelectMany(child => Descendants(child).Prepend(child));
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
        return (new TokenStream(new SourceText("compound.mist", source), [.. tokens]), new DiagnosticBag());
    }
}