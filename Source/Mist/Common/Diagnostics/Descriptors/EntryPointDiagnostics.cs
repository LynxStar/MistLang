#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for entry-point validation.
/// </summary>
public static class EntryPointDiagnostics
{
    /// <summary>
    /// A library project contains top-level statements, which imply an executable
    /// entry point and are not allowed in libraries.
    /// </summary>
    public static readonly DiagnosticDescriptor LibraryWithTopLevelStatements = new(
        new DiagnosticId("MIST_EP_0008"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Library projects cannot have top-level statements. Files: {0}");
}