using System.Collections.Immutable;
using Mist.Backends.Contracts;

namespace Mist.Driver.Contracts;

/// <summary>
/// Options controlling a compilation run.
/// </summary>
/// <param name="ProjectFolder">Root folder of the project being compiled.</param>
/// <param name="SourcePaths">Paths to source files to compile.</param>
/// <param name="Backend">The backend to use for code generation.</param>
/// <param name="OutputDirectory">Directory for intermediate output artifacts.</param>
/// <param name="BinDirectory">Directory for final binary output.</param>
/// <param name="ProjectName">Name of the project.</param>
/// <param name="NamespaceRoot">Root namespace for the project.</param>
/// <param name="EnabledAnalyzerIds">IDs of analyzers to run during compilation.</param>
/// <param name="EmitToDisk">Whether to write output files to disk.</param>
/// <param name="GlobalUsings">
/// Namespaces to implicitly import into every compilation unit.
/// Enables in-process test compilation with global usings without requiring a mist.toml on disk.
/// </param>
/// <param name="Packages">NuGet package references declared in <c>[packages]</c>.</param>
/// <param name="FrameworkReferences">Shared framework references declared in <c>[framework_references]</c>.</param>
/// <param name="TargetFramework">Target framework moniker for dependency resolution (e.g., <c>net10.0</c>).</param>
/// <param name="VerboseResolution">When true, the package-resolution stage reports detailed progress.</param>
/// <param name="CsprojProperties">Arbitrary MSBuild properties declared in <c>[csproj_properties]</c> to emit into the generated csproj.</param>
/// <param name="OutputType">Controls whether the project emits an executable or a class library.</param>
public sealed record CompilerOptions(
    string ProjectFolder,
    ImmutableArray<string> SourcePaths,
    BackendId Backend,
    string OutputDirectory,
    string BinDirectory,
    string ProjectName,
    string NamespaceRoot,
    ImmutableArray<string> EnabledAnalyzerIds,
    bool EmitToDisk = true,
    string? EntryPoint = null,
    ImmutableArray<string> GlobalUsings = default,
    ImmutableArray<NuGetPackageReference> Packages = default,
    ImmutableArray<FrameworkReference> FrameworkReferences = default,
    string TargetFramework = "net10.0",
    bool VerboseResolution = false,
    ImmutableArray<CsprojProperty> CsprojProperties = default,
    ProjectOutputType OutputType = ProjectOutputType.Executable,
    PipelineMode Pipeline = PipelineMode.Classic);