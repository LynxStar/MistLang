using System.Collections.Immutable;

using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// <summary>
/// An append-only collection of diagnostics, drained into an immutable array at stage boundaries.
/// </summary>
public sealed class DiagnosticBag
{
    private List<Diagnostic> diagnostics = [];

    /// <summary>
    /// Adds a diagnostic to the bag.
    /// </summary>
    public void Add(Diagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Reports a diagnostic using a descriptor. Formats the message template
    /// with the supplied arguments and appends the constructed Diagnostic.
    /// </summary>
    public void Report(
        DiagnosticDescriptor descriptor,
        LinePositionSpan location,
        string compilationUnitPath,
        params ReadOnlySpan<object?> args)
    {
        var message = args.Length > 0
            ? string.Format(descriptor.MessageTemplate, args.ToArray())
            : descriptor.MessageTemplate;

        diagnostics.Add(new Diagnostic(
            descriptor.Id,
            descriptor.DefaultSeverity,
            descriptor.Stage,
            message,
            location,
            compilationUnitPath,
            AnalyzerName: null));
    }

    /// <summary>
    /// Reports a diagnostic using a descriptor with an explicit analyzer name.
    /// Used by named analyzers (OwnershipAnalyzer, TypeChecker, etc.).
    /// </summary>
    public void Report(
        DiagnosticDescriptor descriptor,
        LinePositionSpan location,
        string compilationUnitPath,
        string? analyzerName,
        params ReadOnlySpan<object?> args)
    {
        var message = args.Length > 0
            ? string.Format(descriptor.MessageTemplate, args.ToArray())
            : descriptor.MessageTemplate;

        diagnostics.Add(new Diagnostic(
            descriptor.Id,
            descriptor.DefaultSeverity,
            descriptor.Stage,
            message,
            location,
            compilationUnitPath,
            analyzerName));
    }

    /// <summary>
    /// Returns <c>true</c> if any diagnostic with <see cref="DiagnosticSeverity.Error"/> severity has been added.
    /// </summary>
    public bool HasErrors => diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Returns all diagnostics as an immutable array.
    /// </summary>
    public ImmutableArray<Diagnostic> ToImmutableArray()
    {
        return [.. diagnostics];
    }
}