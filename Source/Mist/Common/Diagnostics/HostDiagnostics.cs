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

    public static readonly MistDiagnostic UnhandledException = new(
        DiagnosticNumber.Mint("MIST0423"),
        Title: "Unhandled compiler exception",
        Template: "Internal compiler error: {details}. This is a compiler bug; please report it with the program that caused it.",
        Description: "An unexpected exception escaped the command. The exception type, message and stack are snapshots of the failure; report them with the input so the compiler defect can be reproduced.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("details", SlotKind.Sequence, SlotKind.Text)]);
}