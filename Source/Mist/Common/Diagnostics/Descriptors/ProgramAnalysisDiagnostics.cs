#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class ProgramAnalysisDiagnostics
{
    /// <summary>
    /// A switch expression some value of its scrutinee reaches no arm of. A WARNING, which is classic's
    /// severity for the same finding (MIST_TC_0090): the program compiles, and an unmatched value throws
    /// at runtime. Whether Mist should refuse instead is an open decision recorded on order 3 of
    /// backlog/analysis-v2-capability-progression.yaml, not one this descriptor gets to make.
    /// </summary>
    public static readonly DiagnosticDescriptor SwitchNotExhaustive = new(
        new DiagnosticId("MIST_PA_SWITCH_NOT_EXHAUSTIVE"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "Switch expression is not exhaustive. Missing cases: {0}");
}