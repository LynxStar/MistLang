#pragma warning disable IDE0130
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly DiagnosticDescriptor FieldInComputedProperty = new(
        new DiagnosticId("MIST_PA_FIELD_IN_COMPUTED_PROPERTY"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'field' cannot be used in computed property '{0}': a property whose only accessor is a get with a body has no backing field.");
}