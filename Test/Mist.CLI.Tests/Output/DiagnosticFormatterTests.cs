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
        var diagnostic = DiagnosticFixtures.Occurrence("MIST_PKG_002",
            DiagnosticSeverity.Error,
            PipelineStage.Driver,
            "Package 'BadPkg' version 'not-valid' could not be found",
            location: default,
            compilationUnitPath: "",
            analyzerName: null);

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Equal("error MIST_PKG_002: Package 'BadPkg' version 'not-valid' could not be found", result);
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