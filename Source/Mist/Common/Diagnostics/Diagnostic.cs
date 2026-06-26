using Mist.Common.Text;

namespace Mist.Common.Diagnostics;

/// <summary>
/// A single diagnostic message produced by a compiler pipeline stage.
/// </summary>
public sealed record Diagnostic(
    DiagnosticId Id,
    DiagnosticSeverity Severity,
    PipelineStage Stage,
    string Message,
    LinePositionSpan Location,
    string CompilationUnitPath,
    string? AnalyzerName);