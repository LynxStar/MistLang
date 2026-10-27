#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic DuplicateExtensionMethod = new(
        DiagnosticNumber.Mint("MIST0134"),
        Title: "Duplicate extension method",
        Template: "Extension method '{member}' for receiver '{receiver}' repeats signature {signature}.",
        Description: "Two extension methods on the same receiver type share a name and parameter types, so a call could never choose between them. Remove one, or change a parameter type so the overloads differ.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("receiver", SlotKind.Type), new Slot("signature", SlotKind.Name)]);

    public static readonly MistDiagnostic DuplicateExtensionProperty = new(
        DiagnosticNumber.Mint("MIST0135"),
        Title: "Duplicate extension property",
        Template: "Extension property '{member}' for receiver '{receiver}' is already declared.",
        Description: "Two extension properties on the same receiver type share a name, and a property has no parameters that could tell them apart. Remove one of them, or rename it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("receiver", SlotKind.Type)]);
}