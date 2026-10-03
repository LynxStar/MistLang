#pragma warning disable IDE0130
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly DiagnosticDescriptor StructBaseConstructorRequired = new(
        new DiagnosticId("MIST_STRUCT_BASE_CTOR_REQUIRED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Struct '{0}' requires constructor arguments. Add ': base(...)' to the constructor of '{1}'.");
}