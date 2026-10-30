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

}