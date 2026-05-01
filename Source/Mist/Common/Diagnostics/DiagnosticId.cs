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

    // ── Void type diagnostics ────────────────────────────────────────────

    /// <summary>Void is not permitted as an array element type.</summary>
    public static readonly DiagnosticId VoidArrayNotPermitted = new("MIST_TC_0034");

    // ── Standard Library loading diagnostics ─────────────────────────────

    /// <summary>Stdlib file missing, unreadable, or the stdlib directory is absent.</summary>
    public static readonly DiagnosticId StdlibParse = new("MIST_STDLIB_PARSE");

    /// <summary>
    /// The Standard Library has not been loaded (or a required stdlib type is
    /// absent from the loaded set) at a point in the pipeline that requires
    /// it. This is a fatal, compiler-internal condition: primitive keyword
    /// resolution (Requirement 7.6) and related stdlib-dependent machinery
    /// cannot proceed without the corresponding stdlib type. Raised by
    /// <c>SemanticModelBuilder.LookupQualifiedStdlibType</c> via
    /// <c>StdlibMissingException</c>.
    /// </summary>
    public static readonly DiagnosticId StdlibMissing = new("MIST_STDLIB_MISSING");
}
