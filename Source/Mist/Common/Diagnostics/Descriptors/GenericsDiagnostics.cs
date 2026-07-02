#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for generics-related errors and warnings.
/// Covers type argument arity mismatches, non-generic type argument usage,
/// missing type arguments, duplicate type parameters, and shadowing.
/// </summary>
public static class GenericsDiagnostics
{
    /// <summary>
    /// The number of type arguments provided does not match the number
    /// of type parameters declared on the referenced generic type.
    /// </summary>
    public static readonly DiagnosticDescriptor ArityMismatch = new(
        new DiagnosticId("MIST_ARITY_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type '{0}' expects {1} type argument(s) but received {2}.");

    /// <summary>
    /// Type arguments were provided to a type that is not generic
    /// (i.e., declares zero type parameters).
    /// </summary>
    public static readonly DiagnosticDescriptor NotGeneric = new(
        new DiagnosticId("MIST_NOT_GENERIC"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type '{0}' is not generic and does not accept type arguments.");

    /// <summary>
    /// A generic type is referenced without providing the required type arguments.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingTypeArguments = new(
        new DiagnosticId("MIST_MISSING_TYPE_ARGUMENTS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Generic type '{0}' requires type arguments.");

    /// <summary>
    /// A type parameter list contains two or more parameters with the same name.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateTypeParameter = new(
        new DiagnosticId("MIST_DUPLICATE_TYPE_PARAMETER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Duplicate type parameter name '{0}'.");

    /// <summary>
    /// A type parameter name is identical to the enclosing type name,
    /// which shadows it and may cause confusion.
    /// </summary>
    public static readonly DiagnosticDescriptor TypeParameterShadowsType = new(
        new DiagnosticId("MIST_TYPE_PARAMETER_SHADOWS_TYPE"),
        DiagnosticSeverity.Warning,
        PipelineStage.SemanticModelConstruction,
        "Type parameter '{0}' shadows the enclosing type name.");

    /// <summary>
    /// The number of type arguments provided to a generic method invocation
    /// does not match the number of type parameters declared on the method.
    /// </summary>
    public static readonly DiagnosticDescriptor MethodArityMismatch = new(
        new DiagnosticId("MIST_METHOD_ARITY_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Method '{0}' expects {1} type argument(s) but received {2}.");

    /// <summary>
    /// A type argument identifier could not be resolved to any known type.
    /// </summary>
    public static readonly DiagnosticDescriptor UndefinedTypeArgument = new(
        new DiagnosticId("MIST_UNDEFINED_TYPE_ARGUMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type argument '{0}' could not be resolved.");
}