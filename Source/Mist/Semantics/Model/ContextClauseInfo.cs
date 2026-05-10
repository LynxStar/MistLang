using Mist.Common.Text;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Represents a single with-clause on a method signature.
/// Records the resolved context type, the bound identifier name,
/// and the source span for diagnostic reporting.
/// </summary>
public sealed class ContextClauseInfo
{
    /// <summary>The resolved context type (after alias resolution).</summary>
    public required IType ContextType { get; init; }

    /// <summary>The bound identifier name within the method body.</summary>
    public required string Identifier { get; init; }

    /// <summary>Source span of the with clause for diagnostics.</summary>
    public required SourceSpan Span { get; init; }
}
