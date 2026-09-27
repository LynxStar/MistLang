#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly DiagnosticDescriptor PureScopeWrites = new(
        new DiagnosticId("MIST_PA_PURE_SCOPE_WRITES"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot write {0} from {1}: {2}.");

    public static readonly DiagnosticDescriptor PureScopeReadsMutable = new(
        new DiagnosticId("MIST_PA_PURE_SCOPE_READS_MUTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot read {0} from {1}: {2}.");

    public static readonly DiagnosticDescriptor PureScopeCallsImpure = new(
        new DiagnosticId("MIST_PA_PURE_SCOPE_CALLS_IMPURE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot {0} from {1}: it is {2}; {3} would be needed.");

    public static readonly DiagnosticDescriptor LocalScopeReadsArgument = new(
        new DiagnosticId("MIST_PA_LOCAL_SCOPE_READS_ARGUMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot read {0} through argument '{2}' from {1}: a local scope uses its arguments as values and reads nothing reachable through a reference-typed or by-reference one.");

    public static readonly DiagnosticDescriptor PureScopeStoresUnowned = new(
        new DiagnosticId("MIST_PA_PURE_SCOPE_STORES_UNOWNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot store {0} into an instance the scope owns from {1}: it is {2} and its type can change, so the instance would outlive what it holds; only what the scope owns, or a value that cannot change, may be stored.");

    public static readonly DiagnosticDescriptor PureFunctionRequired = new(
        new DiagnosticId("MIST_PA_PURE_FUNCTION_REQUIRED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot convert '{0}' to '{1}': it is {2}, and a method group converts to a pure function type only when its method is pure.");

    public static readonly DiagnosticDescriptor DefaultNotConsistent = new(
        new DiagnosticId("MIST_PA_DEFAULT_NOT_CONSISTENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0} would not give the same default every time: {2} Every default of '{1}' is evaluated as a pure scope with no inputs.");

    public static readonly DiagnosticDescriptor PurityContractNotMet = new(
        new DiagnosticId("MIST_PA_PURITY_CONTRACT_NOT_MET"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Member '{0}' in type '{1}' must be {2} to satisfy the {2} contract on '{3}' of abstract '{4}': it is {5}.");

    public static readonly DiagnosticDescriptor PureFunctionTypeUntestable = new(
        new DiagnosticId("MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' cannot be tested for or cast to '{1}': a pure function type is a promise the runtime cannot see, so only a value whose static type already states it can be read as one.");

    public static readonly DiagnosticDescriptor PureFunctionTypeUntestableClosing = new(
        new DiagnosticId("MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot close '{0}' of '{1}' with '{2}': '{3}' tests a value of type '{4}' against '{5}', and '{6}' is a promise the runtime cannot see, so the test could read a plain function value as one.");
}