using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Contracts;

/// <param name="GlobalUsings">Lets an in-process test supply global usings without a mist.toml on disk.</param>
/// <param name="RuntimeIdentifier">The runtime identifier named by <c>--rid</c>; <see langword="null"/> derives it from the host and the platform.</param>
public sealed record CompilerOptions(
    string ProjectFolder,
    ImmutableArray<string> SourcePaths,
    BackendId Backend,
    string OutputDirectory,
    string BinDirectory,
    string ProjectName,
    string NamespaceRoot,
    string? EntryPoint = null,
    ImmutableArray<string> GlobalUsings = default,
    ImmutableArray<NuGetPackageReference> Packages = default,
    ImmutableArray<FrameworkReference> FrameworkReferences = default,
    string TargetFramework = "net10.0",
    bool VerboseResolution = false,
    ImmutableArray<CsprojProperty> CsprojProperties = default,
    ProjectOutputType OutputType = ProjectOutputType.Executable,
    PipelineMode Pipeline = PipelineMode.Standard,
    TargetPlatform? Platform = null,
    string? RuntimeIdentifier = null)
{
    public DiagnosticOperation? DiagnosticOperation { get; init; }

    public ProjectConfiguration? Configuration { get; init; }

    public TargetPlatform EffectivePlatform => Platform ?? TargetPlatforms.Host;

    /// <summary>
    /// Assemblies handed to the compiler directly rather than resolved from a declaration. No manifest
    /// can name one, so only a test supplying its own interop fixture sets this.
    /// </summary>
    internal ImmutableArray<string> AssemblyPaths { get; init; } = [];
}