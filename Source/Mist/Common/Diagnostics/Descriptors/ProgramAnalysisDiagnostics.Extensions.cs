#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic DuplicateExtensionMethod = new(
        DiagnosticNumber.Mint("MIST0134"),
        Title: "Duplicate extension method",
        Template: "Extension method '{0}' for receiver '{1}' repeats signature {2} first declared at {3}:{4}:{5}.",
        Description: "Two extension methods on the same receiver type share a name and parameter types, so a call could never choose between them. Remove one, or change a parameter type so the overloads differ.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DuplicateExtensionProperty = new(
        DiagnosticNumber.Mint("MIST0135"),
        Title: "Duplicate extension property",
        Template: "Extension property '{0}' for receiver '{1}' was first declared at {2}:{3}:{4}.",
        Description: "Two extension properties on the same receiver type share a name, and a property has no parameters that could tell them apart. Remove one of them, or rename it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}