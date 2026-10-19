namespace Mist.Common.Diagnostics;

public static partial class ProgramModelDiagnostics
{
    public static readonly DiagnosticDescriptor SpreadValueParameter = new(
        new DiagnosticId("MIST_PM_SPREAD_VALUE_PARAMETER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' uses a spread suffix in a value-parameter declaration. Declare a value pack with 'params' instead: 'params TArgs {0}'.");

    public static readonly DiagnosticDescriptor SpreadNonPackArgument = new(
        new DiagnosticId("MIST_PM_SPREAD_NON_PACK_ARGUMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}...' cannot fill a fixed type-argument slot of '{1}'. A symbolic spread requires a pack slot that accepts every permitted width.");
}