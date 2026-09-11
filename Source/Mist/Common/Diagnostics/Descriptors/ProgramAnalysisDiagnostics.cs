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

    public static readonly DiagnosticDescriptor CastHasNoConversion = new(
        new DiagnosticId("MIST_PA_CAST_HAS_NO_CONVERSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "No conversion is defined from '{0}' to '{1}'. A cast reaches only a type the value already is, or one a declared cast operator reaches; to narrow a value, test it with an 'is' pattern instead.");

    public static readonly DiagnosticDescriptor NoImplicitConversion = new(
        new DiagnosticId("MIST_PA_NO_IMPLICIT_CONVERSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot implicitly convert type '{0}' to '{1}'.");

    public static readonly DiagnosticDescriptor CastRequired = new(
        new DiagnosticId("MIST_PA_CAST_REQUIRED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot implicitly convert type '{0}' to '{1}'. An explicit cast exists (are you missing a cast?)");
}