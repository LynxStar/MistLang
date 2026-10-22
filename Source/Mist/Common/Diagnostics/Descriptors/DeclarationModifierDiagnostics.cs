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

    public static readonly DiagnosticDescriptor RefOnOperatorParameter = new(
        new DiagnosticId("MIST_PM_REF_ON_OPERATOR_PARAMETER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'ref' modifier is not valid on parameter '{0}' of {1}: an operator is applied without an argument list, so no use of it can write the 'ref' a reference argument needs. Take the parameter by value.");

    public static readonly DiagnosticDescriptor ReadonlyRefOnOperatorParameter = new(
        new DiagnosticId("MIST_PM_READONLY_REF_ON_OPERATOR_PARAMETER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'readonly ref' modifier is not valid on parameter '{0}' of {1}: an operator takes its operands by value. Take the parameter by value.");

    public static readonly DiagnosticDescriptor SealedOnOperator = new(
        new DiagnosticId("MIST_PM_SEALED_ON_OPERATOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'sealed' modifier is not valid on an operator declaration.");

    public static readonly DiagnosticDescriptor ReadonlyOnOperator = new(
        new DiagnosticId("MIST_PM_READONLY_ON_OPERATOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'readonly' modifier is not valid on an operator declaration; use 'pure' for a pure operator.");

    public static readonly DiagnosticDescriptor OwnedOnOperator = new(
        new DiagnosticId("MIST_PM_OWNED_ON_OPERATOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'owned' modifier is not valid on an operator declaration.");

    public static readonly DiagnosticDescriptor AffineOnOperator = new(
        new DiagnosticId("MIST_PM_AFFINE_ON_OPERATOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'affine' modifier is not valid on an operator declaration.");

    public static readonly DiagnosticDescriptor RefOnField = new(
        new DiagnosticId("MIST_PM_REF_ON_FIELD"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'ref' modifier is not valid on a field: a reference is a stack-only alias, so no field can store one. Declare the field by value, or take the reference as a ref local where it is used.");

    public static readonly DiagnosticDescriptor StaticOnAbstractMember = new(
        new DiagnosticId("MIST_PM_STATIC_ON_ABSTRACT_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'static' modifier is not valid on {0} '{1}' of abstract type '{2}': an abstract type declares the instance members a conforming type supplies, and a static member is no part of that contract. Declare it on a concrete type instead.");
}