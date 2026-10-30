using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// One admitted report of a descriptor. Built only by <see cref="DiagnosticFactory"/>; its text is
/// rendered on demand from the descriptor and the slot values, so nothing downstream can hold a
/// message the registry did not produce.
/// </summary>
public sealed record Diagnostic
{
    internal Diagnostic(
        MistDiagnostic descriptor,
        ImmutableArray<SlotValue> arguments,
        DiagnosticSeverity severity,
        PipelineStage stage,
        Location location,
        ImmutableArray<RelatedLocation> related,
        ImmutableArray<Frame> frames,
        string? analyzerName,
        ProviderCode? native,
        bool rendersFallback,
        string? legacyText,
        int? cause = null)
    {
        Descriptor = descriptor;
        Cause = cause;
        Arguments = arguments;
        Severity = severity;
        Stage = stage;
        Location = location;
        Related = related.IsDefault ? [] : related;
        Frames = frames.IsDefault ? [] : frames;
        AnalyzerName = analyzerName;
        Native = native;
        RendersFallback = rendersFallback;
        LegacyText = legacyText;
    }

    public int Ordinal { get; private set; }

    public MistDiagnostic Descriptor { get; }

    public DiagnosticNumber Number => Descriptor.Number;

    public ImmutableArray<SlotValue> Arguments { get; }

    public DiagnosticSeverity DefaultSeverity => Descriptor.DefaultSeverity;

    public DiagnosticSeverity Severity { get; private set; }

    public string? SeverityReason { get; private set; }

    public PipelineStage Stage { get; }

    public Location Location { get; }

    public ImmutableArray<RelatedLocation> Related { get; }

    public ImmutableArray<Frame> Frames { get; }

    public string? AnalyzerName { get; }

    public ProviderCode? Native { get; }

    public int? Cause { get; }

    internal bool RendersFallback { get; }

    internal string? LegacyText { get; }

    public string Message => DiagnosticRenderer.Render(this);

    internal void Admit(int ordinal, DiagnosticSeverity effective, string? reason)
    {
        Ordinal = ordinal;
        Severity = effective;
        SeverityReason = reason;
    }

    public bool Equals(Diagnostic? other)
    {
        return other is not null
            && Ordinal == other.Ordinal
            && Cause == other.Cause
            && Descriptor == other.Descriptor
            && Arguments.SequenceEqual(other.Arguments)
            && Severity == other.Severity
            && SeverityReason == other.SeverityReason
            && Stage == other.Stage
            && Location == other.Location
            && Related.SequenceEqual(other.Related)
            && Frames.SequenceEqual(other.Frames)
            && AnalyzerName == other.AnalyzerName
            && Native == other.Native
            && RendersFallback == other.RendersFallback
            && LegacyText == other.LegacyText;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Descriptor.Number, Severity, Stage, Location, Arguments.Length);
    }
}