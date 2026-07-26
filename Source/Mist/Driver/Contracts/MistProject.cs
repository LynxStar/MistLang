namespace Mist.Driver.Contracts;

/// <summary>
/// A Mist project participating in a compilation, as opposed to a single source file.
/// <para>
/// The driver resolves the project graph while loading sources and then flattens it into
/// <see cref="CompilationUnit"/> values, one per file. This records the projects it found
/// so later stages can group units by the project that declared them without inferring the
/// grouping back out of per-file fields.
/// </para>
/// <para>
/// Deliberately flat, with no reference edges and no direct-versus-transitive marking.
/// Mist mirrors C# visibility, where the only boundary is the assembly and reachability
/// does not depend on how many hops away a project sits.
/// </para>
/// </summary>
/// <param name="Name">
/// The project's root namespace, which is the closest thing it has to a readable name.
/// Not an identity: two projects may share one.
/// </param>
/// <param name="ProjectFolder">The directory containing the project's <c>mist.toml</c>.</param>
/// <param name="CanonicalTomlPath">
/// The canonical path of the project's <c>mist.toml</c>. This is the project's identity, and
/// matches <see cref="CompilationUnit.OriginProjectPath"/> on every unit it contributed.
/// </param>
public sealed record MistProject(
    string Name,
    string ProjectFolder,
    string CanonicalTomlPath);