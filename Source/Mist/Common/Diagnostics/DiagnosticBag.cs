using System.Collections.Immutable;

using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// <summary>
/// An append-only collection of diagnostics, drained into an immutable array at stage boundaries.
/// </summary>
public sealed class DiagnosticBag
{
    private List<Diagnostic> diagnostics = [];

    public DiagnosticBag()
        : this(PipelineStage.Driver)
    {
    }

    public DiagnosticBag(PipelineStage stage)
    {
        Stage = stage;
    }

    /// <summary>
    /// The stage currently reporting into this bag. A stage announces itself as it starts, so an
    /// occurrence records who produced it without the descriptor having to guess.
    /// </summary>
    public PipelineStage Stage { get; set; }

    /// <summary>
    /// Forwards an occurrence the factory already built.
    /// </summary>
    public void Add(Diagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
    }

    public void Admit(Admission admission)
    {
        foreach(var occurrence in admission.Occurrences)
        {
            Add(occurrence);
        }
    }

    /// <summary>
    /// Reports a diagnostic using a descriptor, formatting the message template with the supplied
    /// arguments.
    /// <para>
    /// There is deliberately no analyzer-name parameter here, and adding one is unsafe: a lone
    /// <see langword="string"/> argument binds to it ahead of the params array, since an exact match
    /// beats params expansion, so a caller's first format argument would silently be taken as the
    /// name. Analyzers carry a name through
    /// <see cref="DiagnosticBagExtensions.ReportAtNode"/> instead, where a mandatory parameter leaves
    /// overload resolution no such choice to make.
    /// </para>
    /// </summary>
    public void Report(
        MistDiagnostic descriptor,
        LinePositionSpan location,
        string compilationUnitPath,
        params ReadOnlySpan<object?> args)
    {
        Admit(DiagnosticFactory.Create(descriptor, location, compilationUnitPath, Stage, LegacyArguments.For(descriptor, args).AsSpan()));
    }

    /// <summary>
    /// What <paramref name="action"/> reports, returned rather than added, for a caller that decides
    /// afterwards which of them the program should see.
    /// </summary>
    public List<Diagnostic> Withhold(Action action)
    {
        var kept = diagnostics;
        diagnostics = [];

        try
        {
            action();
            return diagnostics;
        }
        finally
        {
            diagnostics = kept;
        }
    }

    public bool Contains(Diagnostic diagnostic)
    {
        return diagnostics.Exists(existing => existing.Number == diagnostic.Number
            && existing.Location == diagnostic.Location
            && existing.CompilationUnitPath == diagnostic.CompilationUnitPath);
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