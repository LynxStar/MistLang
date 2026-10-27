#pragma warning disable IDE0130
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic FieldInComputedProperty = new(
        DiagnosticNumber.Mint("MIST0136"),
        Title: "Field keyword in computed property",
        Template: "'field' cannot be used in computed property '{property}': a property whose only accessor is a get with a body has no backing field.",
        Description: "A property that consists of a single bodied getter is recomputed on every read and owns no storage, so the 'field' keyword inside it has nothing to refer to. Return the computed value directly, or add a setter or an initializer so the property becomes a stored one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("property", SlotKind.Name)]);
}