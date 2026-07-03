#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the entry point analyzer.
/// Covers top-level statement validation, Main method resolution,
/// and entry_point configuration conflicts.
/// </summary>
public static class EntryPointDiagnostics
{
    /// <summary>
    /// Multiple files contain top-level statements in the same project.
    /// Only one file per project may have top-level statements.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleTopLevelStatementFiles = new(
        new DiagnosticId("MIST_EP_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Multiple files contain top-level statements. Only one file per project may have top-level statements. Files: {0}");

    /// <summary>
    /// Project has both top-level statements and a static void Main method
    /// but no <c>entry_point</c> configuration in mist.toml to resolve the conflict.
    /// </summary>
    public static readonly DiagnosticDescriptor TlsAndMainWithoutConfig = new(
        new DiagnosticId("MIST_EP_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Project has both top-level statements (in '{0}') and a static void Main (in '{1}'). Add entry_point = \"top-level\" to mist.toml to resolve this conflict.");

    /// <summary>
    /// A top-level statement file exists but <c>entry_point</c> specifies a class name.
    /// When top-level statements are present, the entry point must be "top-level".
    /// </summary>
    public static readonly DiagnosticDescriptor TlsWithClassEntryPoint = new(
        new DiagnosticId("MIST_EP_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot specify entry_point = \"{0}\" when a top-level statement file exists. A top-level statement file must always be the entry point. Use entry_point = \"top-level\" instead.");

    /// <summary>
    /// Multiple classes contain <c>static void Main</c> and no <c>entry_point</c>
    /// configuration is present in mist.toml to disambiguate.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleMainsWithoutConfig = new(
        new DiagnosticId("MIST_EP_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Multiple classes contain static void Main: {0}. Add entry_point = \"{1}\" to mist.toml to specify which one to use.");

    /// <summary>
    /// The <c>entry_point = "top-level"</c> setting is specified in mist.toml
    /// but no file in the project contains top-level statements.
    /// </summary>
    public static readonly DiagnosticDescriptor TopLevelEntryPointButNoTls = new(
        new DiagnosticId("MIST_EP_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "entry_point = \"top-level\" specified in mist.toml but no file contains top-level statements.");

    /// <summary>
    /// A top-level statement file contains a block namespace declaration.
    /// Only file-scoped namespaces are allowed in TLS files.
    /// </summary>
    public static readonly DiagnosticDescriptor TlsWithBlockNamespace = new(
        new DiagnosticId("MIST_EP_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Top-level statements cannot coexist with block namespace declarations in the same file. Use a file-scoped namespace (namespace X;) instead.");

    /// <summary>
    /// The <c>entry_point</c> specifies a class name that either does not exist
    /// or does not contain a <c>static void Main</c> method.
    /// </summary>
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
}