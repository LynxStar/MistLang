using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>
/// A project found while resolving the project graph, recorded so later stages can group
/// <see cref="CompilationUnit"/> values by project without inferring it from per-file fields.
/// <para>
/// Deliberately flat, with no reference edges and no direct-versus-transitive marking.
/// Mist mirrors C# visibility, where the only boundary is the assembly and reachability
/// does not depend on how many hops away a project sits.
/// </para>
/// </summary>
/// <param name="Name">The root namespace, the closest thing to a readable name. Not an identity: two projects may share one.</param>
/// <param name="CanonicalTomlPath">
/// The project's identity; matches <see cref="CompilationUnit.OriginProjectPath"/> on every unit it contributed.
/// </param>
public sealed record MistProject(
    string Name,
    string ProjectFolder,
    string CanonicalTomlPath)
{
    /// <summary>
    /// Applies to this project's files only: two projects in one compilation may declare different
    /// ones, and neither sees the other's.
    /// </summary>
    public ImmutableArray<string> GlobalUsings { get; init; } = ImmutableArray<string>.Empty;

    public string? EntryPoint { get; init; }

    public ProjectOutputType OutputType { get; init; } = ProjectOutputType.Executable;
}