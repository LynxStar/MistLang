namespace Mist.Common.Diagnostics;

public static class HostDiagnostics
{
    public static readonly MistDiagnostic InvalidDiagnosticControl = new(
        DiagnosticNumber.Mint("MIST0422"),
        Title: "Invalid diagnostic control",
        Template: "{reason}",
        Description: "A diagnostic command-line control has an invalid value, selector or command. Correct the option before running the command.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("reason", SlotKind.Text)]);
}