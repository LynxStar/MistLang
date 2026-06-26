using Mist.Common.Diagnostics;

namespace Mist.Semantics.Model;

/// <summary>
/// Thrown by <see cref="SemanticModelBuilder"/> when a required Standard
/// Library type cannot be located in the semantic model — either because the
/// stdlib has not been loaded at all, or because the specific type requested
/// is not present in the <c>Standard</c> namespace.
///
/// <para>
/// This is a fatal, compiler-internal contract violation: primitive keyword
/// resolution (Requirement 7.6) and other stdlib-dependent machinery cannot
/// proceed without the stdlib. Carrying the <see cref="DiagnosticId"/>
/// <c>MIST_STDLIB_MISSING</c> lets the driver surface a structured
/// diagnostic to the user rather than an anonymous runtime error.
/// </para>
/// </summary>
public sealed class StdlibMissingException : Exception
{
    /// <summary>
    /// The stable diagnostic identifier for this fatal condition
    /// (<c>MIST_STDLIB_MISSING</c>).
    /// </summary>
    public DiagnosticId DiagnosticId { get; }

    /// <summary>
    /// The fully-qualified stdlib type name that was requested but could not
    /// be resolved (e.g., <c>"Standard.Int32"</c>).
    /// </summary>
    public string QualifiedName { get; }

    public StdlibMissingException(string qualifiedName, string message)
        : base(message)
    {
        DiagnosticId = Common.Diagnostics.DiagnosticId.StdlibMissing;
        QualifiedName = qualifiedName;
    }
}