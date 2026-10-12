#pragma warning disable IDE0130 // Namespace intentionally flat so reporting sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class CallableSignatureDiagnostics
{
    public static readonly DiagnosticDescriptor DuplicateOperatorSignature = new(
        new DiagnosticId("MIST_PA_DUPLICATE_OPERATOR_SIGNATURE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' already declares operator '{1}' with parameter types {2}; the return type does not distinguish overloads. Earlier declaration: {3}({4},{5}).");

    public static readonly DiagnosticDescriptor DuplicateCastSignature = new(
        new DiagnosticId("MIST_PA_DUPLICATE_CAST_OPERATOR"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' already declares an {1} cast operator from '{2}' to '{3}'. Earlier declaration: {4}({5},{6}).");
}