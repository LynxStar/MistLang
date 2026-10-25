#pragma warning disable IDE0130
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic StructBaseConstructorRequired = new(
        DiagnosticNumber.Mint("MIST0147"),
        Title: "Struct base constructor required",
        Template: "Struct '{0}' requires constructor arguments. Add ': base(...)' to the constructor of '{1}'.",
        Description: "A struct inherits from a struct whose constructor takes parameters, and the derived constructor forwards nothing to it, so the inherited state would be left unconstructed. Give the derived constructor a base initializer that supplies the arguments the ancestor requires.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}