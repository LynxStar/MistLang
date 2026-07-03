namespace Mist.Common.Diagnostics;

/// <summary>
/// Bundles all static metadata for a compiler diagnostic: its stable ID,
/// default severity, originating pipeline stage, and a positional message
/// template. Emission sites supply only the dynamic parts (location, format args).
/// </summary>
public readonly record struct DiagnosticDescriptor
{
    public DiagnosticId Id { get; }
    public DiagnosticSeverity DefaultSeverity { get; }
    public PipelineStage Stage { get; }
    public string MessageTemplate { get; }

    public DiagnosticDescriptor(
        DiagnosticId id,
        DiagnosticSeverity defaultSeverity,
        PipelineStage stage,
        string messageTemplate)
    {
        if(string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException(
                "DiagnosticId.Value must not be null, empty, or whitespace.", nameof(id));
        }

        if(string.IsNullOrWhiteSpace(messageTemplate))
        {
            throw new ArgumentException(
                "MessageTemplate must not be null, empty, or whitespace.", nameof(messageTemplate));
        }

        Id = id;
        DefaultSeverity = defaultSeverity;
        Stage = stage;
        MessageTemplate = messageTemplate;
    }
}