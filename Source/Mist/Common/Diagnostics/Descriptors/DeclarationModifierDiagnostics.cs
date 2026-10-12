#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class DeclarationModifierDiagnostics
{
    public static readonly DiagnosticDescriptor RefOnOperator = new(
        new DiagnosticId("MIST_PM_REF_ON_OPERATOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'ref' modifier is not valid on an operator: an operator returns its result by value, and only a method can return a reference.");

    public static readonly DiagnosticDescriptor RefOnField = new(
        new DiagnosticId("MIST_PM_REF_ON_FIELD"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'ref' modifier is not valid on a field: a reference is a stack-only alias, so no field can store one. Declare the field by value, or take the reference as a ref local where it is used.");
}