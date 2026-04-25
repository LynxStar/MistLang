namespace Storm.SourceGenerator.Diagnostics;

/// <summary>
/// Stable diagnostic identifiers for all Storm source-generator diagnostics.
/// Each property is used as the <see cref="StormDiagnostic.Id"/> value and maps
/// to a Roslyn <c>DiagnosticDescriptor</c> when reported at build time.
/// </summary>
public static class StormDiagnosticIds
{
    /// <summary>A Storm grammar file contains a syntax error.</summary>
    public static string STORM_SYNTAX_ERROR { get; } = "STORM0001";

    /// <summary>An import path could not be resolved.</summary>
    public static string STORM_IMPORT_NOT_FOUND { get; } = "STORM0002";

    /// <summary>Two unnamespaced declarations share the same name.</summary>
    public static string STORM_DUPLICATE_NAME { get; } = "STORM0003";

    /// <summary>A reference to a rule, token, or fragment could not be resolved.</summary>
    public static string STORM_UNDEFINED_REFERENCE { get; } = "STORM0004";

    /// <summary>Direct or indirect left-recursion that cannot be lowered to LL(*).</summary>
    public static string STORM_LEFT_RECURSION { get; } = "STORM0005";

    /// <summary>An alternative is statically unreachable under PEG ordered-choice semantics.</summary>
    public static string STORM_UNREACHABLE_ALTERNATIVE { get; } = "STORM0006";

    /// <summary>A grammar ambiguity that cannot be resolved via bounded lookahead.</summary>
    public static string STORM_AMBIGUOUS_RULE { get; } = "STORM0007";

    /// <summary>A grammar construct that the source generator cannot lower to C#.</summary>
    public static string STORM_UNSUPPORTED_CONSTRUCT { get; } = "STORM0008";

    /// <summary>No file declares a <c>grammar</c> directive.</summary>
    public static string STORM_NO_ROOT_GRAMMAR { get; } = "STORM0009";

    /// <summary>Multiple files declare a <c>grammar</c> directive.</summary>
    public static string STORM_MULTIPLE_ROOT_GRAMMARS { get; } = "STORM0010";
}
