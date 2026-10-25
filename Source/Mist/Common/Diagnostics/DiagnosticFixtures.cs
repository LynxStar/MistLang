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
        var descriptor = Descriptor(code, message.Replace("{", "{{").Replace("}", "}}"), severity);

        return DiagnosticFactory.Create(descriptor, location, compilationUnitPath, stage, [], analyzerName).Occurrence;
    }
}