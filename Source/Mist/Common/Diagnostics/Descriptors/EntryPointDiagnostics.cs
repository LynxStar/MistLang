#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for entry-point validation.
/// </summary>
public static class EntryPointDiagnostics
{
    public static readonly DiagnosticDescriptor MultipleTopLevelFiles = new(
        new DiagnosticId("MIST_EP_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Multiple files contain top-level statements. Only one file per project may have top-level statements. Files: {0}");

    public static readonly DiagnosticDescriptor TopLevelStatementsBesideMain = new(
        new DiagnosticId("MIST_EP_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Project has both top-level statements (in '{0}') and a static void Main (in '{1}'). Add entry_point = \"top-level\" to mist.toml to resolve this conflict.");

    public static readonly DiagnosticDescriptor ClassEntryPointBesideTopLevelStatements = new(
        new DiagnosticId("MIST_EP_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot specify entry_point = \"{0}\" when a top-level statement file exists. A top-level statement file must always be the entry point. Use entry_point = \"top-level\" instead.");

    public static readonly DiagnosticDescriptor MultipleMains = new(
        new DiagnosticId("MIST_EP_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Multiple classes contain static void Main: {0}. Add entry_point = \"{1}\" to mist.toml to specify which one to use.");

    public static readonly DiagnosticDescriptor TopLevelEntryPointWithoutStatements = new(
        new DiagnosticId("MIST_EP_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "entry_point = \"top-level\" specified in mist.toml but no file contains top-level statements.");

    public static readonly DiagnosticDescriptor TopLevelStatementsWithBlockNamespace = new(
        new DiagnosticId("MIST_EP_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "File '{0}' has both top-level statements and a block namespace declaration: {1}. Top-level statements cannot coexist with block namespace declarations in the same file. Use a file-scoped namespace (namespace X;) instead.");

    public static readonly DiagnosticDescriptor EntryPointClassNotFound = new(
        new DiagnosticId("MIST_EP_0007"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "entry_point specifies '{0}' but no class with that name contains a static void Main method.");

    /// <summary>
    /// A library project contains top-level statements, which imply an executable
    /// entry point and are not allowed in libraries.
    /// </summary>
    public static readonly DiagnosticDescriptor LibraryWithTopLevelStatements = new(
        new DiagnosticId("MIST_EP_0008"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Library projects cannot have top-level statements. Files: {0}");

    /// <summary>
    /// Apart from <see cref="MultipleMains"/> because <c>entry_point</c> names a class: when the class
    /// that is the entry point declares two methods one can be, nothing in the manifest chooses.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateEntryPoint = new(
        new DiagnosticId("MIST_PM_DUPLICATE_ENTRY_POINT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is the program's entry point but declares more than one 'Main' that can be one. A program can have only one entry point.");
}