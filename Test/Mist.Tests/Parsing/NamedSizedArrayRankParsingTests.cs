using Mist.Generated;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class NamedSizedArrayRankParsingTests
{
    private static readonly (TokenKind, string) Open = (TokenKind.LEFT_BRACKET, "[");
    private static readonly (TokenKind, string) Close = (TokenKind.RIGHT_BRACKET, "]");
    private static readonly (TokenKind, string) OpenParen = (TokenKind.LEFT_PARENTHESIS, "(");
    private static readonly (TokenKind, string) CloseParen = (TokenKind.RIGHT_PARENTHESIS, ")");

    [Fact]
    public void ALiteralSizeIsTheSizedRankItWas()
    {
        var (tokens, diagnostics) = Tokens(Open, (TokenKind.INTEGER_LITERAL, "3"), Close);

        var rank = Assert.IsType<SizedArrayRankArrayRankSuffixNode>(MistParser.ParseArrayRankSuffix(tokens, diagnostics));

        Assert.Equal("3", rank.Size.Lexeme);
        Assert.Empty(diagnostics.Entries);
    }

    [Fact]
    public void ANameSizesTheRank()
    {
        var (tokens, diagnostics) = Tokens(Open, Name("n"), Close);

        var rank = Assert.IsType<NamedSizedArrayRankArrayRankSuffixNode>(MistParser.ParseArrayRankSuffix(tokens, diagnostics));

        Assert.Equal("n", rank.Length.IDENTIFIER.Lexeme);
        Assert.Empty(rank.Length.IDENTIFIERs);
        Assert.Empty(diagnostics.Entries);
    }

    [Fact]
    public void ADottedPathOfNamesSizesTheRank()
    {
        var (tokens, diagnostics) = Tokens(Open, Name("source"), (TokenKind.DOT, "."), Name("Length"), Close);

        var rank = Assert.IsType<NamedSizedArrayRankArrayRankSuffixNode>(MistParser.ParseArrayRankSuffix(tokens, diagnostics));

        Assert.Equal("source", rank.Length.IDENTIFIER.Lexeme);
        Assert.Equal("Length", Assert.Single(rank.Length.IDENTIFIERs).Lexeme);
        Assert.Empty(diagnostics.Entries);
    }

    [Fact]
    public void AnArrayTypeTakesTheNamedRankInATypePosition()
    {
        var (tokens, diagnostics) = Tokens((TokenKind.INT, "int"), Open, Name("n"), Close);

        var type = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(MistParser.ParseSingleTypeRef(tokens, diagnostics));

        Assert.Equal("n", Assert.IsType<NamedSizedArrayRankArrayRankSuffixNode>(type.ArrayRank).Length.IDENTIFIER.Lexeme);
        Assert.Empty(diagnostics.Entries);
    }

    [Fact]
    public void ACastToAnArraySizedByANameIsACast()
    {
        var (tokens, diagnostics) = Tokens(OpenParen, (TokenKind.INT, "int"), Open, Name("n"), Close, CloseParen, Name("a"));

        var cast = Assert.IsType<CastExpressionUnaryNode>(MistParser.ParseUnary(tokens, diagnostics));

        _ = Assert.IsType<NamedSizedArrayRankArrayRankSuffixNode>(Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(cast.TargetType.First).ArrayRank);
        Assert.Empty(diagnostics.Entries);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AParenthesizedElementReadBeforeAnOperandStaysAnElementRead(int depth)
    {
        var parts = Enumerable.Repeat(OpenParen, depth)
            .Concat([Name("a"), Open, Name("i"), Close])
            .Concat(Enumerable.Repeat(CloseParen, depth))
            .Concat([(TokenKind.MINUS, "-"), (TokenKind.INTEGER_LITERAL, "1")]);
        var (tokens, diagnostics) = Tokens([.. parts]);

        var read = Assert.IsType<PostfixExpressionUnaryNode>(MistParser.ParseUnary(tokens, diagnostics));

        _ = Assert.IsType<ParenthesizedExpressionPrimaryNode>(read.Primary.Target);
        _ = Assert.Single(Descendants(read).OfType<IndexSuffixPostfixSuffixNode>());
        Assert.Empty(Descendants(read).OfType<NamedSizedArrayRankArrayRankSuffixNode>());
    }

    [Fact]
    public void AnElementReadBetweenComparisonsInACallStaysTwoComparisons()
    {
        var (tokens, diagnostics) = Tokens(
            Name("Use"), OpenParen, Name("x"), (TokenKind.LESS_THAN, "<"), Name("a"), Open, Name("i"), Close, (TokenKind.COMMA, ","),
            Name("y"), (TokenKind.GREATER_THAN, ">"), OpenParen, Name("z"), CloseParen, CloseParen);

        var call = Assert.IsType<PostfixNode>(MistParser.ParsePostfix(tokens, diagnostics));

        _ = Assert.IsType<IdentifierExpressionPrimaryNode>(call.Target);
        var invocation = Assert.IsType<InvocationSuffixPostfixSuffixNode>(Assert.Single(call.Suffix));
        Assert.Equal(2, invocation.Arguments!.GetAllExpressions().Length);
        Assert.Empty(Descendants(call).OfType<GenericInvocationExpressionPrimaryNode>());
    }

    [Fact]
    public void APackLengthIsNotSizedByAName()
    {
        var (tokens, diagnostics) = Tokens(Name("Type"), Open, Name("n"), Close, (TokenKind.COLON, ":"), Name("Ts"));

        Assert.IsNotType<ExplicitPackTypeParameterTypeParameterNode>(MistParser.ParseTypeParameter(tokens, diagnostics));
    }

    private static (TokenKind, string) Name(string text)
    {
        return (TokenKind.IDENTIFIER, text);
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
        return (new TokenStream(new SourceText("rank.mist", source), [.. tokens]), new DiagnosticBag());
    }
}