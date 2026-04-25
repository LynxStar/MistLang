namespace Storm.SourceGenerator.Diagnostics;

/// <summary>
/// Stable diagnostic identifiers for all Storm source-generator diagnostics.
/// Each constant is used as the <see cref="StormDiagnostic.Id"/> value and maps
/// to a Roslyn <c>DiagnosticDescriptor</c> when reported at build time.
/// </summary>
public static class StormDiagnosticIds
{
    /// <summary>A Storm grammar file contains a syntax error.</summary>
    public const string STORM_SYNTAX_ERROR = "STORM0001";

    /// <summary>An import path could not be resolved.</summary>
    public const string STORM_IMPORT_NOT_FOUND = "STORM0002";

    /// <summary>Two unnamespaced declarations share the same name.</summary>
    public const string STORM_DUPLICATE_NAME = "STORM0003";

    /// <summary>A reference to a rule, token, or fragment could not be resolved.</summary>
    public const string STORM_UNDEFINED_REFERENCE = "STORM0004";

    /// <summary>Direct or indirect left-recursion that cannot be lowered to LL(*).</summary>
    public const string STORM_LEFT_RECURSION = "STORM0005";

    /// <summary>An alternative is statically unreachable under PEG ordered-choice semantics.</summary>
    public const string STORM_UNREACHABLE_ALTERNATIVE = "STORM0006";

    /// <summary>A grammar ambiguity that cannot be resolved via bounded lookahead.</summary>
    public const string STORM_AMBIGUOUS_RULE = "STORM0007";

    /// <summary>A grammar construct that the source generator cannot lower to C#.</summary>
    public const string STORM_UNSUPPORTED_CONSTRUCT = "STORM0008";
}
