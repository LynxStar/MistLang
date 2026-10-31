#pragma warning disable IDE0130
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic NativeSlotNotFilled = new(
        DiagnosticNumber.Mint("MIST0459"),
        Title: "CLR slot not filled",
        Template: "'{type}' does not fill slot '{slot}' of '{base}': the CLR declares it abstract and no member of '{type}' or a base answers it.",
        Description: "A CLR base class declares an abstract member, and neither the type nor any Mist type between it and that base declares a member of the same name, signature, return type and accessibility to fill it, so the runtime would have nothing to dispatch to. Declare the member with the slot's signature and accessibility.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("slot", SlotKind.Name), new Slot("base", SlotKind.Type)]);

    public static readonly MistDiagnostic NativeSlotSealed = new(
        DiagnosticNumber.Mint("MIST0460"),
        Title: "CLR slot sealed",
        Template: "'{type}.{member}' cannot fill slot '{slot}' of '{base}': the CLR declares it sealed.",
        Description: "The member repeats the signature of a CLR base member that a sealed override has closed to further filling, and Mist does not hide an inherited member by redeclaration. Rename the member, or remove it and use the inherited one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("slot", SlotKind.Name), new Slot("base", SlotKind.Type)]);

    public static readonly MistDiagnostic NativeSlotShape = new(
        DiagnosticNumber.Mint("MIST0461"),
        Title: "CLR slot fill shape mismatch",
        Template: "'{type}.{member}' fills slot '{slot}' of '{base}' but differs in {difference}: the slot declares {declared}.",
        Description: "The member repeats the name and parameters of a virtual or abstract CLR base member, so it fills that slot, but it differs from the slot in return type, accessibility, being static, an accessor the slot lacks, an accessor the slot has that an auto-property omits, or a type parameter constraint the slot does not promise. Change the member to match what the slot declares.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("slot", SlotKind.Name), new Slot("base", SlotKind.Type), new Slot("difference", SlotKind.Enum), new Slot("declared", SlotKind.Name)]);
}