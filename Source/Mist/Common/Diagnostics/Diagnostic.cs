using System.Collections.Immutable;
using Mist.Common.Source;

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
        LinePositionSpan location,
        string compilationUnitPath,
        string? analyzerName,
        ProviderCode? native,
        bool rendersFallback,
        string? legacyText)
    {
        Descriptor = descriptor;
        Arguments = arguments;
        Severity = severity;
        Stage = stage;
        Location = location;
        CompilationUnitPath = compilationUnitPath;
        AnalyzerName = analyzerName;
        Native = native;
        RendersFallback = rendersFallback;
        LegacyText = legacyText;
    }

    public MistDiagnostic Descriptor { get; }

    public DiagnosticNumber Number => Descriptor.Number;

    public ImmutableArray<SlotValue> Arguments { get; }

    public DiagnosticSeverity DefaultSeverity => Descriptor.DefaultSeverity;

    public DiagnosticSeverity Severity { get; init; }

    public PipelineStage Stage { get; }

    public LinePositionSpan Location { get; }

    public string CompilationUnitPath { get; }

    public string? AnalyzerName { get; }

    public ProviderCode? Native { get; }

    internal bool RendersFallback { get; }

    internal string? LegacyText { get; }

    public string Message => DiagnosticRenderer.Render(this);

    public bool Equals(Diagnostic? other)
    {
        return other is not null
            && Descriptor == other.Descriptor
            && Arguments.SequenceEqual(other.Arguments)
            && Severity == other.Severity
            && Stage == other.Stage
            && Location == other.Location
            && CompilationUnitPath == other.CompilationUnitPath
            && AnalyzerName == other.AnalyzerName
            && Native == other.Native
            && RendersFallback == other.RendersFallback
            && LegacyText == other.LegacyText;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Descriptor.Number, Severity, Stage, Location, CompilationUnitPath, Arguments.Length);
    }
}