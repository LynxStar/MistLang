using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>
/// A fully resolved project reference: canonical <c>mist.toml</c> path,
/// project folder, parsed configuration, and source file paths.
/// </summary>
/// <param name="CanonicalTomlPath">Canonical absolute path to the referenced <c>mist.toml</c>.</param>
/// <param name="ProjectFolder">Directory containing the <c>mist.toml</c>.</param>
/// <param name="Configuration">Parsed project configuration.</param>
/// <param name="SourcePaths">All <c>.mist</c> files in <paramref name="ProjectFolder"/> (sorted, stable).</param>
public sealed record ResolvedProjectReference(
    string CanonicalTomlPath,
    string ProjectFolder,
    ProjectConfiguration Configuration,
    ImmutableArray<string> SourcePaths);
