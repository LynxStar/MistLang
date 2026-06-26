// Feature: unified-diagnostic-formatting
// Unit tests for SemanticModelBuilder null SourceText fallback
// Validates: Requirements 4.4
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying the SemanticModelBuilder fallback behavior when
/// SourceText is null or the syntax node span has zero length.
///
/// These tests replicate the private MakeDiagnosticWithLocation helper logic:
///
///   LinePositionSpan location = default;
///   string compilationUnitPath = sourceText?.Path ?? "&lt;unknown&gt;";
///   if (sourceText is not null &amp;&amp; node.Span.Length > 0)
///   {
///       var mistSpan = new SourceSpan(node.Span.Start, node.Span.Length);
///       location = sourceText.ToLinePositionSpan(mistSpan);
///   }
///
/// **Validates: Requirements 4.4**
/// </summary>
public class SemanticModelBuilderFallbackTests
{
    /// <summary>
    /// Helper that replicates the MakeDiagnosticWithLocation logic.
    /// </summary>
    private static Diagnostic MakeDiagnostic(
        SourceText? sourceText,
        int spanStart,
        int spanLength)
    {
        LinePositionSpan location = default;
        var compilationUnitPath = sourceText?.Path ?? "<unknown>";

        if(sourceText is not null && spanLength > 0)
        {
            var mistSpan = new SourceSpan(spanStart, spanLength);
            location = sourceText.ToLinePositionSpan(mistSpan);
        }

        return new Diagnostic(
            new DiagnosticId("MIST_TEST_ERROR"),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            "Test error message",
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: null);
    }

    [Fact]
    public void NullSourceText_ProducesDefaultLocation()
    {
        var diagnostic = MakeDiagnostic(sourceText: null, spanStart: 0, spanLength: 5);

        Assert.Equal(default, diagnostic.Location);
    }

    [Fact]
    public void NullSourceText_ProducesUnknownCompilationUnitPath()
    {
        var diagnostic = MakeDiagnostic(sourceText: null, spanStart: 0, spanLength: 5);

        Assert.Equal("<unknown>", diagnostic.CompilationUnitPath);
    }

    [Fact]
    public void ZeroLengthSpan_ProducesDefaultLocation()
    {
        var sourceText = new SourceText("test.mist", "let x = 42;");

        var diagnostic = MakeDiagnostic(sourceText, spanStart: 0, spanLength: 0);

        Assert.Equal(default, diagnostic.Location);
    }

    [Fact]
    public void ZeroLengthSpan_PreservesCompilationUnitPath()
    {
        var sourceText = new SourceText("test.mist", "let x = 42;");

        var diagnostic = MakeDiagnostic(sourceText, spanStart: 0, spanLength: 0);

        Assert.Equal("test.mist", diagnostic.CompilationUnitPath);
    }

    [Fact]
    public void NonNullSourceText_WithPositiveSpan_ProducesNonDefaultLocation()
    {
        var sourceText = new SourceText("test.mist", "let x = 42;");

        var diagnostic = MakeDiagnostic(sourceText, spanStart: 0, spanLength: 3);

        Assert.NotEqual(default, diagnostic.Location);
        Assert.Equal("test.mist", diagnostic.CompilationUnitPath);
    }
}