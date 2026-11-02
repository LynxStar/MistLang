using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>
/// The structured outcome of resolving the declared <c>[packages]</c> entries.
/// Carries the flattened assembly paths (direct packages plus transitive
/// dependencies), a per-declared-package summary, and the count of declared
/// packages that failed to resolve.
/// </summary>
/// <param name="AssemblyPaths">
/// All assembly file paths contributed by every successfully resolved package
/// and its transitive dependencies.
/// </param>
/// <param name="Resolved">
/// One entry per declared package that was located without an error-severity
/// diagnostic, including packages that contributed zero compatible assemblies
/// (recorded with <see cref="ResolvedPackageInfo.AssemblyCount"/> == 0).
/// </param>
/// <param name="FailureCount">
/// The number of declared packages that produced an error-severity diagnostic
/// during resolution.
/// </param>
public sealed record PackageResolutionResult(
    ImmutableArray<string> AssemblyPaths,
    ImmutableArray<ResolvedPackageInfo> Resolved,
    int FailureCount)
{
    public ImmutableArray<RuntimeAsset> RuntimeAssets { get; init; } = [];

    /// <summary>The SDK graph the restore named; <see langword="null"/> when no restore ran or the graph could not be read.</summary>
    public RidGraph? RuntimeIdentifierGraph { get; init; }

    public ImmutableDictionary<string, string> PackageDirectories { get; init; } =
        ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A per-declared-package resolution summary used by the verbose
/// package-resolution stage and the <c>mist restore</c> command.
/// </summary>
/// <param name="Name">The declared NuGet package name.</param>
/// <param name="Version">The resolved, normalized package version.</param>
/// <param name="SelectedTfm">
/// The package <c>lib/</c> target-framework moniker that was selected for the
/// Target_Framework, or an empty string when the package contributed no
/// assemblies.
/// </param>
/// <param name="AssemblyCount">
/// The number of assemblies the package directly contributed for the selected
/// TFM. A value of 0 indicates the package resolved but contributed no
/// compatible assemblies.
/// </param>
public sealed record ResolvedPackageInfo(
    string Name,
    string Version,
    string SelectedTfm,
    int AssemblyCount);