using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// An append-only collection of diagnostics, drained into an immutable array at stage boundaries.
/// </summary>
public sealed class DiagnosticBag
{
    private List<Diagnostic> _diagnostics = [];

    /// <summary>
    /// Adds a diagnostic to the bag.
    /// </summary>
    public void Add(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Returns <c>true</c> if any diagnostic with <see cref="DiagnosticSeverity.Error"/> severity has been added.
    /// </summary>
    public bool HasErrors => _diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Returns all diagnostics as an immutable array.
    /// </summary>
    public ImmutableArray<Diagnostic> ToImmutableArray() => [.. _diagnostics];
}
