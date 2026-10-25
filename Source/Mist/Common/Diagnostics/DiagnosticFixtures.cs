using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// <summary>
/// The one way a test builds a diagnostic of its own. A fixture is never a registry field, so the
/// index, the catalog and the validator never see it.
/// </summary>
internal static class DiagnosticFixtures
{
    public static MistDiagnostic Descriptor(
        string code,
        string template,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        DiagnosticCategory category = DiagnosticCategory.Typ,
        PolicyClass policy = PolicyClass.Fixed,
        string title = "Test fixture",
        params Slot[] slots)
    {
        return new MistDiagnostic(
            DiagnosticNumber.Mint(code),
            Title: title,
            Template: template,
            Description: "A descriptor a test constructed for its own purposes.",
            Category: category,
            Policy: policy,
            DefaultSeverity: severity,
            Slots: [.. slots]);
    }

    public static Diagnostic Occurrence(
        string code,
        DiagnosticSeverity severity,
        PipelineStage stage,
        string message,
        LinePositionSpan location = default,
        string compilationUnitPath = "",
        string? analyzerName = null)
    {
        var placed = location == default ? LegacyLocation.Map(compilationUnitPath) : At(compilationUnitPath, location);

        return Occurrence(code, severity, stage, message, placed, analyzerName);
    }

    public static Diagnostic Occurrence(
        string code,
        DiagnosticSeverity severity,
        PipelineStage stage,
        string message,
        Location location,
        string? analyzerName = null)
    {
        var descriptor = Descriptor(code, message.Replace("{", "{{").Replace("}", "}}"), severity);

        return DiagnosticFactory.Create(descriptor, location, stage, [], analyzerName: analyzerName).Occurrence;
    }

    public static SourceRange At(string path, LinePositionSpan span)
    {
        var lines = Math.Max(span.Start.Line, span.End.Line);
        var width = Math.Max(span.Start.Column, span.End.Column);
        var text = new SourceText(path, string.Join("\n", Enumerable.Repeat(new string(' ', width), lines)));
        var start = (span.Start.Line - 1) * (width + 1) + span.Start.Column - 1;
        var end = (span.End.Line - 1) * (width + 1) + span.End.Column - 1;

        return new SourceRange(new SourceIdentity(text), start, Math.Max(0, end - start));
    }
}