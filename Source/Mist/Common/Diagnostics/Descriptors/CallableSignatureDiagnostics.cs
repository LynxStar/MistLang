#pragma warning disable IDE0130 // Namespace intentionally flat so reporting sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class CallableSignatureDiagnostics
{
    public static readonly MistDiagnostic DuplicateOperatorSignature = new(
        DiagnosticNumber.Mint("MIST0029"),
        Title: "Duplicate operator signature",
        Template: "Type '{0}' already declares operator '{1}' with parameter types {2}; the return type does not distinguish overloads. Earlier declaration: {3}({4},{5}).",
        Description: "A type declares the same operator twice over the same parameter types, and operators overload only on their parameters, so the second declaration can never be chosen. Remove one of the two or change a parameter type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DuplicateCastSignature = new(
        DiagnosticNumber.Mint("MIST0030"),
        Title: "Duplicate cast operator",
        Template: "Type '{0}' already declares an {1} cast operator from '{2}' to '{3}'. Earlier declaration: {4}({5},{6}).",
        Description: "A type declares two cast operators of the same kind between the same source and target types, so a cast could not choose between them. Remove one, or make one of them implicit and the other explicit if both conversions are meant to exist.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}