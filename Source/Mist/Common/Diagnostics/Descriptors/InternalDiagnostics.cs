#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class InternalDiagnostics
{
    public static readonly MistDiagnostic ArgumentSchemaBreach = new(
        DiagnosticNumber.Mint("MIST_ICE_ARGUMENT_SCHEMA_BREACH"),
        Title: "Diagnostic arguments breach schema",
        Template: "Internal compiler error: the arguments reported for diagnostic '{number}' by stage '{stage}' do not match its slot schema ({reason}), so its message was replaced by a fallback. This is a compiler bug; please report it with the program that caused it.",
        Description: "A compiler stage reported a diagnostic with arguments that do not fit the descriptor's declared slots, so the intended message could not be rendered and a fallback text stands in for it. The program is not at fault; report the compiler bug together with the program that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("number", SlotKind.Name), new Slot("stage", SlotKind.Enum), new Slot("reason", SlotKind.Text)]);
}