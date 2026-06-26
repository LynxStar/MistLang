using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.CLI.Tests.Output;

public class DiagnosticFormatterTests
{
    [Fact]
    public void Format_WithSourceLocation_IncludesPathAndPosition()
    {
        var diagnostic = new Diagnostic(
            new DiagnosticId("MIST001"),
            DiagnosticSeverity.Error,
            PipelineStage.Parsing,
            "Unexpected token",
            new LinePositionSpan(new LinePosition(10, 5), new LinePosition(10, 8)),
            CompilationUnitPath: "src/main.mist",
            AnalyzerName: null);

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Equal("src/main.mist(10,5): error MIST001: Unexpected token", result);
    }

    [Fact]
    public void Format_WithoutSourceLocation_OmitsPosition()
    {
        var diagnostic = new Diagnostic(
            new DiagnosticId("MIST002"),
            DiagnosticSeverity.Warning,
            PipelineStage.Analysis,
            "Unused variable",
            Location: default,
            CompilationUnitPath: "src/main.mist",
            AnalyzerName: null);

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Equal("src/main.mist: warning MIST002: Unused variable", result);
    }

    [Fact]
    public void Format_WithoutPath_OmitsPathEntirely()
    {
        var diagnostic = new Diagnostic(
            new DiagnosticId("MIST003"),
            DiagnosticSeverity.Error,
            PipelineStage.Driver,
            "Unknown backend",
            Location: default,
            CompilationUnitPath: "<unknown>",
            AnalyzerName: null);

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Equal("error MIST003: Unknown backend", result);
    }

    [Fact]
    public void Format_WarningSeverity_UsesWarningLabel()
    {
        var diagnostic = new Diagnostic(
            new DiagnosticId("MIST004"),
            DiagnosticSeverity.Warning,
            PipelineStage.Analysis,
            "Potential issue",
            Location: default,
            CompilationUnitPath: "<unknown>",
            AnalyzerName: "TestAnalyzer");

        var result = DiagnosticFormatter.Format(diagnostic);

        Assert.Contains("warning", result);
    }
}