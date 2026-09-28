#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly DiagnosticDescriptor DuplicateExtensionMethod = new(
        new DiagnosticId("MIST_PA_DUPLICATE_EXTENSION_METHOD"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Extension method '{0}' for receiver '{1}' repeats signature {2} first declared at {3}:{4}:{5}.");

    public static readonly DiagnosticDescriptor DuplicateExtensionProperty = new(
        new DiagnosticId("MIST_PA_DUPLICATE_EXTENSION_PROPERTY"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Extension property '{0}' for receiver '{1}' was first declared at {2}:{3}:{4}.");
}