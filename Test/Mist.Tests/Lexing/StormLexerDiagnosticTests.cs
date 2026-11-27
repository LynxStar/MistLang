using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Storm.Runtime;
using DiagnosticBag = Mist.Common.Diagnostics.DiagnosticBag;
using SourceText = Mist.Common.Source.SourceText;

namespace Mist.Tests.Lexing;

[Trait("Tier", "1")]
public sealed class StormLexerDiagnosticTests
{
    private const string AttributionSource = "xx#\nx$+@";
    private static readonly ImmutableArray<Token> PartialMatches =
    [
        CreateToken(Generated.TokenKind.Error, 2, 1, 3, "#"),
        CreateToken(Generated.TokenKind.Error, 5, 2, 2, "$"),
        CreateToken(Generated.TokenKind.PLUS, 6, 2, 3, "+"),
    ];

    [Theory]
    [InlineData("@", 0, 1, 1)]
    [InlineData("x\r\n@", 3, 2, 1)]
    public void ErrorTokensSupplyAuthoritativeRangesAndSpelling(string contents, int offset, int line, int column)
    {
        var source = new SourceText("error.mist", contents);
        var diagnostics = new DiagnosticBag(PipelineStage.Lexing);
        var error = new Token((int)Generated.TokenKind.Error, new Storm.Runtime.SourceSpan(offset, 1),
            new Storm.Runtime.LinePosition(line, column), new Storm.Runtime.LinePosition(line, column + 1), TokenChannel.Default, "@");

        StormLexerDiagnostics.Report(source, "opaque runtime text", source.Path, line, column, [error], diagnostics);

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Equal("opaque runtime text", finding.Message);
        Assert.Equal("opaque runtime text", Assert.IsType<TextValue>(Assert.Single(finding.Arguments)).Text);
        var range = Assert.IsType<SourceRange>(finding.Location);
        Assert.Same(source, range.File.Text);
        Assert.Equal(offset, range.Offset);
        Assert.Equal(1, range.Length);
        Assert.Equal(new StormEntryFrame(offset, 1, "@"), Assert.Single(finding.Frames));
    }

    [Fact]
    public void MatchingErrorSuppliesRangeAndFrameAfterPartialMatches()
    {
        var source = new SourceText("source.mist", AttributionSource);
        var diagnostics = new DiagnosticBag(PipelineStage.Lexing);
        var error = CreateToken(Generated.TokenKind.Error, 6, 2, 3, "+@");

        StormLexerDiagnostics.Report(source, "unstructured", source.Path, 2, 3, [.. PartialMatches, error], diagnostics);

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Equal(new SourceRange(new SourceIdentity(source), 6, 2), finding.Location);
        Assert.Equal(new StormEntryFrame(6, 2, "+@"), Assert.Single(finding.Frames));
    }

    [Fact]
    public void OnlyPartialMatchesLeaveSourcePointWithoutFrame()
    {
        var source = new SourceText("source.mist", AttributionSource);
        var diagnostics = new DiagnosticBag(PipelineStage.Lexing);

        StormLexerDiagnostics.Report(source, "unstructured", source.Path, 2, 3, PartialMatches, diagnostics);

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Equal(new SourcePoint(new SourceIdentity(source), 6), finding.Location);
        Assert.Empty(finding.Frames);
    }

    [Fact]
    public void EntriesWithoutAnErrorTokenKeepTheAvailablePoint()
    {
        var source = new SourceText("source.mist", "x");
        var diagnostics = new DiagnosticBag(PipelineStage.Lexing);

        StormLexerDiagnostics.Report(source, "unstructured", null, 1, 1, [], diagnostics);

        Assert.Equal(new SourcePoint(new SourceIdentity(source), 0), Assert.Single(diagnostics.Admitted).Location);
    }

    [Fact]
    public void AnUnlocatableRuntimeEntryKeepsItsFileAndDoesNotInventARange()
    {
        var source = new SourceText("source.mist", "x");
        var diagnostics = new DiagnosticBag(PipelineStage.Lexing);

        StormLexerDiagnostics.Report(source, "unstructured", "other.mist", 1, 1, [], diagnostics);

        Assert.Equal(new FileOnly("other.mist"), Assert.Single(diagnostics.Admitted).Location);
    }
    [Fact]
    public void AnInvalidAuthoritativeTokenRangeReachesTheFactoryBoundsCheck()
    {
        var source = new SourceText("source.mist", "x");
        var diagnostics = new DiagnosticBag(PipelineStage.Lexing);
        var error = new Token((int)Generated.TokenKind.Error, new Storm.Runtime.SourceSpan(2, 1),
            new Storm.Runtime.LinePosition(1, 1), new Storm.Runtime.LinePosition(1, 2), TokenChannel.Default, "@");

        StormLexerDiagnostics.Report(source, "opaque runtime text", source.Path, 1, 1, [error], diagnostics);

        Assert.Collection(diagnostics.Admitted,
            finding => Assert.Equal(new Absent(AbsenceReason.OutOfBounds), finding.Location),
            finding => Assert.Same(InternalDiagnostics.LocationOutOfBounds, finding.Descriptor));
    }

    private static Token CreateToken(Generated.TokenKind kind, int offset, int line, int column, string lexeme)
    {
        return new Token((int)kind, new Storm.Runtime.SourceSpan(offset, lexeme.Length),
            new Storm.Runtime.LinePosition(line, column), new Storm.Runtime.LinePosition(line, column + lexeme.Length),
            TokenChannel.Default, lexeme);
    }
}