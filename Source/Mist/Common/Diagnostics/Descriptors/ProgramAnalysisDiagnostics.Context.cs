#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly DiagnosticDescriptor MissingContext = new(
        new DiagnosticId("MIST_CTX_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0} '{1}' requires context of type '{2}', but no matching context declaration is in scope at {3}.");

    public static readonly DiagnosticDescriptor DuplicateContextBinding = new(
        new DiagnosticId("MIST_CTX_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0} '{1}' has duplicate context binding for type '{2}': '{3}' already binds it at {4}.");

    public static readonly DiagnosticDescriptor ContextTypeNotNameable = new(
        new DiagnosticId("MIST_CTX_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot determine a context type from this expression: its type '{0}' holds an anonymous type, which no `with` clause can name.");

    public static readonly DiagnosticDescriptor ContextVariableNotReassignable = new(
        new DiagnosticId("MIST_CTX_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Context-bound variable '{0}' is not reassignable{1}.");

    public static readonly DiagnosticDescriptor ContextContractNotMet = new(
        new DiagnosticId("MIST_CTX_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Method '{0}' on type '{1}' must declare the same `with` clauses as the slot it fills on abstract '{2}': {3}.");
}