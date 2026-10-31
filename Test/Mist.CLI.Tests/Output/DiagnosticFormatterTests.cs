using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.CLI.Tests.Output;

[Trait("Tier", "1")]
public class DiagnosticFormatterTests
{
    [Fact]
    public void Format_WithSourceLocation_IncludesPathAndPosition()
    {
        var diagnostic = DiagnosticFixtures.Occurrence("MIST001",
            DiagnosticSeverity.Error,
            PipelineStage.Parsing,
            "Unexpected token",
            new LinePositionSpan(new LinePosition(10, 5), new LinePosition(10, 8)),
            compilationUnitPath: "src/main.mist",
            analyzerName: null);

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Equal("src/main.mist(10,5): error MIST001: Unexpected token", result);
    }

    [Fact]
    public void Format_WithoutSourceLocation_OmitsPosition()
    {
        var diagnostic = DiagnosticFixtures.Occurrence("MIST002",
            DiagnosticSeverity.Warning,
            PipelineStage.Analysis,
            "Unused variable",
            location: default,
            compilationUnitPath: "src/main.mist",
            analyzerName: null);

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Equal("src/main.mist: warning MIST002: Unused variable", result);
    }

    [Fact]
    public void Format_WithoutPath_OmitsPathEntirely()
    {
        var diagnostic = DiagnosticFixtures.Occurrence("MIST003",
            DiagnosticSeverity.Error,
            PipelineStage.Driver,
            "Unknown backend",
            location: default,
            compilationUnitPath: "<unknown>",
            analyzerName: null);

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Equal("error MIST003: Unknown backend", result);
    }

    [Fact]
    public void Format_WithEmptyPath_OmitsPathEntirely()
    {
        var diagnostic = DiagnosticFixtures.Occurrence("MIST0036",
            DiagnosticSeverity.Error,
            PipelineStage.Driver,
            "Package 'BadPkg' version 'not-valid' could not be found",
            location: default,
            compilationUnitPath: "",
            analyzerName: null);

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Equal("error MIST0036: Package 'BadPkg' version 'not-valid' could not be found", result);
    }

    [Theory]
    [InlineData(false, "Components/Counter.razor(2,3): error MIST005: Mapped")]
    [InlineData(true, "Components/Counter.razor(2,3): error MIST005: Mapped (translated: out/translated/Counter.razor.mist(58,25))")]
    public void Format_MappedGeneratedLocation_PrintsTheMappedPositionAndTheTranslatedOneOnlyWhenVerbose(bool verbose, string expected)
    {
        var razor = new SourceText("Components/Counter.razor", "<p>\n  @count\n</p>");
        var mapped = new SourceRange(new SourceIdentity(razor), 6, 6);
        var native = new LinePositionSpan(new LinePosition(58, 25), new LinePosition(58, 30));
        var diagnostic = DiagnosticFixtures.Occurrence("MIST005", DiagnosticSeverity.Error, PipelineStage.Analysis, "Mapped",
            new GeneratedLocation("out/translated/Counter.razor.mist", native, mapped));

        Assert.Equal(expected, DiagnosticFormatter.Format(diagnostic, verbose));
    }

    [Fact]
    public void Format_Verbose_LeavesEveryUnmappedShapeAsItIs()
    {
        var ranged = DiagnosticFixtures.Occurrence("MIST006", DiagnosticSeverity.Error, PipelineStage.Parsing, "Ranged",
            new LinePositionSpan(new LinePosition(10, 5), new LinePosition(10, 8)), compilationUnitPath: "src/main.mist");
        var generated = DiagnosticFixtures.Occurrence("MIST007", DiagnosticSeverity.Error, PipelineStage.Backend, "CS1",
            new GeneratedLocation("out/Program.cs", new LinePositionSpan(new LinePosition(3, 4), new LinePosition(3, 5)), null));

        Assert.Equal("src/main.mist(10,5): error MIST006: Ranged", DiagnosticFormatter.Format(ranged, verbose: true));
        Assert.Equal("out/Program.cs: error MIST007: CS1", DiagnosticFormatter.Format(generated, verbose: true));
    }

    [Fact]
    public void Format_WarningSeverity_UsesWarningLabel()
    {
        var diagnostic = DiagnosticFixtures.Occurrence("MIST004",
            DiagnosticSeverity.Warning,
            PipelineStage.Analysis,
            "Potential issue",
            location: default,
            compilationUnitPath: "<unknown>",
            analyzerName: "TestAnalyzer");

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Contains("warning", result);
    }
}