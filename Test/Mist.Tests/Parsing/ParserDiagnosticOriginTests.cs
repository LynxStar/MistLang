using Mist.Common.Diagnostics;
using Mist.Parsing;
using Storm.Runtime;
using DiagnosticBag = Mist.Common.Diagnostics.DiagnosticBag;
using SourceText = Mist.Common.Source.SourceText;
using TokenKind = Mist.Generated.TokenKind;
using TokenStream = Mist.Lexing.Contracts.TokenStream;

namespace Mist.Tests.Parsing;

[Trait("Tier", "2")]
public sealed class ParserDiagnosticOriginTests
{
    [Fact]
    public void NodeValidationFindingsHaveParsedOriginsBeforeTheyAreAdmitted()
    {
        var source = new SourceText("modifiers.mist", "public private class C {}");
        var tokens = new (TokenKind Kind, string Text, int Offset)[]
        {
            (TokenKind.PUBLIC, "public", 0),
            (TokenKind.PRIVATE, "private", 7),
            (TokenKind.CLASS, "class", 15),
            (TokenKind.IDENTIFIER, "C", 21),
            (TokenKind.LEFT_BRACE, "{", 23),
            (TokenKind.RIGHT_BRACE, "}", 24),
            (TokenKind.EndOfFile, "", 25),
        };
        var stream = new TokenStream(source, [.. tokens.Select(item => new Token(
            (int)item.Kind, new SourceSpan(item.Offset, item.Text.Length),
            new LinePosition(1, item.Offset + 1),
            new LinePosition(1, item.Offset + item.Text.Length + 1),
            TokenChannel.Default, item.Text))]);
        var diagnostics = new DiagnosticBag();

        var parsed = new MistParser().Parse(stream, diagnostics);

        Assert.NotNull(parsed.Root);
        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(ParserDiagnostics.MultipleAccessModifiers, finding.Descriptor);
        var location = Assert.IsType<SourceRange>(finding.Location);
        Assert.Same(source, location.File.Text);
        Assert.Equal(0, location.Offset);
        Assert.True(location.Length > 0);
    }
}