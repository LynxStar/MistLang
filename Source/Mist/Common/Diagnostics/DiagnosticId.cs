namespace Mist.Common.Diagnostics;

/// <summary>
/// A stable identifier for a compiler diagnostic.
/// </summary>
public readonly record struct DiagnosticId(string Value)
{
    // ── Generics diagnostics ─────────────────────────────────────────────

    /// <summary>Type argument count doesn't match type parameter count.</summary>
    public static readonly DiagnosticId ArityMismatch = new("MIST_ARITY_MISMATCH");

    /// <summary>Type arguments provided to a non-generic type.</summary>
    public static readonly DiagnosticId NotGeneric = new("MIST_NOT_GENERIC");

    /// <summary>Generic type used without type arguments.</summary>
    public static readonly DiagnosticId MissingTypeArguments = new("MIST_MISSING_TYPE_ARGUMENTS");

    /// <summary>Duplicate type parameter name in a type parameter list.</summary>
    public static readonly DiagnosticId DuplicateTypeParameter = new("MIST_DUPLICATE_TYPE_PARAMETER");

    /// <summary>Type parameter name shadows the enclosing type name.</summary>
    public static readonly DiagnosticId TypeParameterShadowsType = new("MIST_TYPE_PARAMETER_SHADOWS_TYPE");

    /// <summary>Method type argument count doesn't match method type parameter count.</summary>
    public static readonly DiagnosticId MethodArityMismatch = new("MIST_METHOD_ARITY_MISMATCH");

    /// <summary>Type argument cannot be resolved to a known type.</summary>
    public static readonly DiagnosticId UndefinedTypeArgument = new("MIST_UNDEFINED_TYPE_ARGUMENT");
}
