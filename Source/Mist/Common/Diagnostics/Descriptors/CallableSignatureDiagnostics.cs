#pragma warning disable IDE0130 // Namespace intentionally flat so reporting sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class CallableSignatureDiagnostics
{
    public static readonly MistDiagnostic DuplicateOperatorSignature = new(
        DiagnosticNumber.Mint("MIST0029"),
        Title: "Duplicate operator signature",
        Template: "Type '{type}' already declares operator '{operator}' with parameter types ({parameters}); the return type does not distinguish overloads.",
        Description: "A type declares the same operator twice over the same parameter types, and operators overload only on their parameters, so the second declaration can never be chosen. Remove one of the two or change a parameter type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("operator", SlotKind.Name), new Slot("parameters", SlotKind.Sequence, SlotKind.Type)]);

    public static readonly MistDiagnostic DuplicateCastSignature = new(
        DiagnosticNumber.Mint("MIST0030"),
        Title: "Duplicate cast operator",
        Template: "Type '{type}' already declares an {kind} cast operator from '{source}' to '{target}'.",
        Description: "A type declares two cast operators of the same kind between the same source and target types, so a cast could not choose between them. Remove one, or make one of them implicit and the other explicit if both conversions are meant to exist.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("kind", SlotKind.Enum), new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)]);
}