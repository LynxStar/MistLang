namespace Mist.Common.Diagnostics;

/// <summary>
/// Diagnostic descriptors for the C# transpiler backend, compilation driver,
/// and assembly type provider. Covers emission template errors, Roslyn compilation
/// failures, assembly load failures, and general backend exceptions.
/// </summary>
public static class BackendDiagnostics
{
    /// <summary>
    /// An intrinsic member was invoked but the backend has no emission
    /// template registered for it. The compilation must fail with no output artifact.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingIntrinsicEmissionTemplate = new(
        new DiagnosticId("MIST_BE_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "No emission template found for intrinsic '{0}' in backend 'CSharp'.");

    /// <summary>
    /// A method implicitly overrides a base class method and was emitted with
    /// the <c>override</c> modifier. Informational notice to the developer.
    /// </summary>
    public static readonly DiagnosticDescriptor ImplicitOverrideInfo = new(
        new DiagnosticId("MIST_BE_INFO_OVERRIDE"),
        DiagnosticSeverity.Info,
        PipelineStage.Backend,
        "Method '{0}' implicitly overrides '{1}' (emitted as '{2} override'). If this was not intentional, rename the method.");

    /// <summary>
    /// An unhandled exception occurred in the C# transpiler backend.
    /// The compilation cannot produce an output artifact.
    /// </summary>
    public static readonly DiagnosticDescriptor BackendError = new(
        new DiagnosticId("MIST_BACKEND_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "C# transpiler backend failed: {0}");

    /// <summary>
    /// No C# source files were found in the output directory for Roslyn compilation.
    /// </summary>
    public static readonly DiagnosticDescriptor BuildError = new(
        new DiagnosticId("MIST_BUILD_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "No C# source files found to compile.");

    /// <summary>
    /// A Roslyn (C#) compilation error was produced during the downstream
    /// build of emitted C# source files.
    /// </summary>
    public static readonly DiagnosticDescriptor CSharpError = new(
        new DiagnosticId("MIST_CS_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "{0}");

    /// <summary>
    /// An assembly failed to load via <c>MetadataLoadContext</c> during
    /// ambient type provider initialization.
    /// </summary>
    public static readonly DiagnosticDescriptor AssemblyLoadFailed = new(
        new DiagnosticId("MIST_ASM_LOAD_FAILED"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Failed to load assembly '{0}': {1}");
}