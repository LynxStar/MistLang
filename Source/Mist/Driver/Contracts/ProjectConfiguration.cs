using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Contracts;

/// <summary>
/// A parsed <c>mist.toml</c>. Each parameter carries the manifest key of the same name in snake_case.
/// </summary>
/// <param name="NamespaceRoot">Defaults to <paramref name="Name"/>.</param>
/// <param name="EntryPoint"><c>"top-level"</c>, or the fully qualified name of the class whose <c>Main</c> runs.</param>
/// <param name="Diagnostics">Already validated against the diagnostic registry.</param>
public sealed record ProjectConfiguration(
    string Name,
    string NamespaceRoot,
    ImmutableArray<ProjectReference> References,
    string? EntryPoint = null,
    ImmutableArray<string> GlobalUsings = default,
    ImmutableArray<NuGetPackageReference> Packages = default,
    ImmutableArray<FrameworkReference> FrameworkReferences = default,
    string TargetFramework = "net10.0",
    ImmutableArray<CsprojProperty> CsprojProperties = default,
    ProjectOutputType OutputType = ProjectOutputType.Executable,
    DiagnosticPolicy? Diagnostics = null)
{
    public DiagnosticPolicy Diagnostics { get; } = Diagnostics ?? DiagnosticPolicy.Default;
}