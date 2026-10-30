using System.Collections.Immutable;

using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// <summary>
/// An append-only collection of diagnostics, drained into an immutable array at stage boundaries.
/// </summary>
public sealed class DiagnosticBag
{
    private List<Diagnostic> diagnostics = [];
    private readonly DiagnosticOwnership ownership = new();

    public DiagnosticBag()
        : this(new DiagnosticOperation(), PipelineStage.Driver)
    {
    }

    public DiagnosticBag(PipelineStage stage)
        : this(new DiagnosticOperation(), stage)
    {
    }

    public DiagnosticBag(DiagnosticOperation operation, PipelineStage stage = PipelineStage.Driver)
    {
        Operation = operation;
        Stage = stage;
    }

    public DiagnosticOperation Operation { get; }

    /// <summary>
    /// The stage currently reporting into this bag. A stage announces itself as it starts, so an
    /// occurrence records who produced it without the descriptor having to guess.
    /// </summary>
    public PipelineStage Stage { get; set; }

    /// <summary>
    /// Admits an occurrence the factory already built: one not yet admitted anywhere receives the
    /// operation's next ordinal, and one forwarded from another bag keeps the ordinal it has.
    /// </summary>
    public void Add(Diagnostic diagnostic)
    {
        if(diagnostic.Ordinal == 0)
        {
            var (effective, reason) = SeverityPolicy.Apply(diagnostic.Descriptor, Operation.Policy);
            diagnostic.Admit(Operation.Next(), effective, reason);
        }

        diagnostics.Add(diagnostic);
    }

    public IDisposable ReplayOwnedFailures()
    {
        return ownership.Replaying(diagnostics.Select(occurrence => occurrence.Ordinal).ToHashSet());
    }

    public void AdmitOwned(object owner, Admission admission)
    {
        if(admission.Breaches.IsEmpty && ownership.Previous(owner, admission.Occurrence.Descriptor) is { } previous)
        {
            Add(previous);
            return;
        }

        Admit(admission);
        ownership.Record(owner, admission.Occurrence);
    }

    public void Replay(Diagnostic occurrence)
    {
        Add(occurrence);
    }

    public void Replay(Diagnostic occurrence, object owner)
    {
        AdmitOwned(owner, new Admission(occurrence, []));
    }

    public void ReportTyped(MistDiagnostic descriptor, Location location, params ReadOnlySpan<SlotValue> arguments)
    {
        Admit(DiagnosticFactory.Create(descriptor, location, Stage, arguments));
    }

    public IReadOnlyList<Diagnostic> Admitted => diagnostics;

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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The span stays in the adapter's signature for the producers Phase 2 migrates; without the text it was computed against it cannot be placed.")]
    public void Report(
        MistDiagnostic descriptor,
        LinePositionSpan location,
        string compilationUnitPath,
        params ReadOnlySpan<object?> args)
    {
        ReportAt(descriptor, LegacyLocation.Map(compilationUnitPath), args);
    }

    public void ReportAt(MistDiagnostic descriptor, Location location, params ReadOnlySpan<object?> args)
    {
        Admit(DiagnosticFactory.Create(descriptor, location, Stage, LegacyArguments.For(descriptor, args).AsSpan()));
    }

    /// <summary>
    /// What <paramref name="action"/> reports, held back rather than added, for a caller that decides
    /// afterwards whether to commit, discard or replay it. Withholds nest, and an exception inside
    /// the action leaves the parent exactly as it was.
    /// </summary>
    public Withheld Withhold(Action action)
    {
        var kept = diagnostics;
        diagnostics = [];

        try
        {
            action();
            return new Withheld(this, [.. diagnostics]);
        }
        finally
        {
            diagnostics = kept;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if any admitted occurrence has the effective severity <see cref="DiagnosticSeverity.Error"/>.
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