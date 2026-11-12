using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <param name="AssemblyPaths">Includes the transitive dependencies of every resolved package.</param>
/// <param name="Resolved">
/// Declared packages only, including one that resolved with an <see cref="ResolvedPackageInfo.AssemblyCount"/> of zero.
/// </param>
public sealed record PackageResolutionResult(
    ImmutableArray<string> AssemblyPaths,
    ImmutableArray<ResolvedPackageInfo> Resolved,
    int FailureCount)
{
    public ImmutableArray<RuntimeAsset> RuntimeAssets { get; init; } = [];

    /// <summary>The SDK graph the restore named; <see langword="null"/> when no restore ran or the graph could not be read.</summary>
    public RidGraph? RuntimeIdentifierGraph { get; init; }
}

/// <param name="Version">Resolved and normalized, not the declared string.</param>
/// <param name="SelectedTfm">The <c>lib/</c> folder chosen; empty when the package contributed no assembly.</param>
/// <param name="AssemblyCount">This package's own assemblies, excluding its dependencies'.</param>
public sealed record ResolvedPackageInfo(
    string Name,
    string Version,
    string SelectedTfm,
    int AssemblyCount);