#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the readonly context analyzer.
/// Covers field assignment, method call, getter call, setter call, constructor allocation,
/// and parameter-level readonly enforcement.
/// </summary>
public static class ReadonlyDiagnostics
{
    /// <summary>Field assignment within a readonly context.</summary>
    public static readonly DiagnosticDescriptor FieldAssignmentInReadonlyContext = new(
        new DiagnosticId("MIST_RO_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Non-readonly method call within a readonly context.</summary>
    public static readonly DiagnosticDescriptor NonReadonlyMethodCallInReadonlyContext = new(
        new DiagnosticId("MIST_RO_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Non-readonly getter call within a readonly context.</summary>
    public static readonly DiagnosticDescriptor NonReadonlyGetterCallInReadonlyContext = new(
        new DiagnosticId("MIST_RO_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Setter call within a readonly context.</summary>
    public static readonly DiagnosticDescriptor SetterCallInReadonlyContext = new(
        new DiagnosticId("MIST_RO_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Non-readonly constructor allocation within a readonly context.</summary>
    public static readonly DiagnosticDescriptor NonReadonlyConstructorAllocation = new(
        new DiagnosticId("MIST_RO_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Non-readonly method call on a readonly parameter.</summary>
    public static readonly DiagnosticDescriptor NonReadonlyMethodOnReadonlyParam = new(
        new DiagnosticId("MIST_RO_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Field assignment on a readonly parameter.</summary>
    public static readonly DiagnosticDescriptor FieldAssignmentOnReadonlyParam = new(
        new DiagnosticId("MIST_RO_0007"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Setter call on a readonly parameter.</summary>
    public static readonly DiagnosticDescriptor SetterCallOnReadonlyParam = new(
        new DiagnosticId("MIST_RO_0008"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Readonly field assignment outside of a constructor.</summary>
    public static readonly DiagnosticDescriptor ReadonlyFieldAssignmentOutsideConstructor = new(
        new DiagnosticId("MIST_RO_0009"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Field assignment within a readonly block (constructor privilege suspended).</summary>
    public static readonly DiagnosticDescriptor FieldAssignmentInReadonlyBlock = new(
        new DiagnosticId("MIST_RO_0010"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Non-readonly access on a closed generic with unknown type argument.</summary>
    public static readonly DiagnosticDescriptor ClosedUnknownNonReadonlyAccess = new(
        new DiagnosticId("MIST_TC_0062"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");
}