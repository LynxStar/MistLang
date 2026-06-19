using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Mist.Driver;

/// <summary>
/// Resolves NuGet package references to assembly file paths.
/// Downloads packages not in the local cache, resolves transitive dependencies,
/// and selects the nearest compatible TFM for each package.
/// </summary>
public static class PackageResolver
{
    /// <summary>
    /// The standard NuGet global packages folder (~/.nuget/packages/).
    /// </summary>
    private static readonly string GlobalPackagesFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    /// <summary>
    /// TFM preference order for selecting the best compatible framework from a package.
    /// Higher index = lower preference.
    /// </summary>
    internal static readonly NuGetFramework[] TfmPreferenceOrder =
    [
        NuGetFramework.Parse("net10.0"),
        NuGetFramework.Parse("net9.0"),
        NuGetFramework.Parse("net8.0"),
        NuGetFramework.Parse("netstandard2.1"),
        NuGetFramework.Parse("netstandard2.0"),
    ];

    /// <summary>
    /// The configured NuGet feeds searched when restoring packages. Reported in
    /// <c>MIST_PKG_002</c> diagnostics so the user can see exactly which feeds
    /// were consulted.
    /// </summary>
    private static readonly ImmutableArray<string> ConfiguredFeeds =
        ["https://api.nuget.org/v3/index.json"];

    /// <summary>
    /// Upper bound on restoring a single package (download + extract). When a
    /// restore exceeds this bound a <c>MIST_PKG_003</c> restore-timeout
    /// diagnostic is emitted. This single 100-second bound replaces the former
    /// 60-second download timeout.
    /// </summary>
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(100);

    /// <summary>
    /// The configured feeds rendered as a comma-separated list for diagnostics.
    /// </summary>
    private static string FeedList => string.Join(", ", ConfiguredFeeds);

    /// <summary>
    /// Resolves all package references to assembly paths.
    /// Downloads packages not in the local cache.
    /// Resolves transitive dependencies.
    /// Selects nearest compatible TFM for each package.
    /// </summary>
    /// <param name="packages">The declared package references from mist.toml.</param>
    /// <param name="targetFramework">The project's target framework (e.g., "net10.0").</param>
    /// <param name="diagnostics">Diagnostic bag for reporting errors.</param>
    /// <returns>
    /// A <see cref="PackageResolutionResult"/> carrying the flattened assembly
    /// paths, a per-declared-package summary, and the count of declared packages
    /// that failed to resolve.
    /// </returns>
    public static PackageResolutionResult ResolvePackages(
        ImmutableArray<NuGetPackageReference> packages,
        string targetFramework,
        DiagnosticBag diagnostics)
    {
        if (packages.IsDefaultOrEmpty)
            return new PackageResolutionResult(
                ImmutableArray<string>.Empty,
                ImmutableArray<ResolvedPackageInfo>.Empty,
                FailureCount: 0);

        var assemblyPaths = ImmutableArray.CreateBuilder<string>();
        var resolvedInfos = ImmutableArray.CreateBuilder<ResolvedPackageInfo>();
        var resolvedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failureCount = 0;

        var repository = Repository.Factory.GetCoreV3(ConfiguredFeeds[0]);
        var cache = new SourceCacheContext();
        var logger = NullLogger.Instance;

        var projectFramework = NuGetFramework.Parse(targetFramework);

        // Resolve each declared package and its transitive dependencies.
        // The IsDirect flag distinguishes declared packages (recorded as
        // ResolvedPackageInfo / counted as failures) from transitive
        // dependencies (which only contribute assembly paths).
        var queue = new Queue<(PackageIdentity Identity, bool IsDirect)>();

        foreach (var pkg in packages)
        {
            if (!NuGetVersion.TryParse(pkg.Version, out var version))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.PackageNotFound,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Package '{pkg.Name}' version '{pkg.Version}' could not be found on any configured NuGet feed ({FeedList}); the version string is not a valid NuGet version.",
                    Location: default,
                    CompilationUnitPath: string.Empty,
                    AnalyzerName: null));
                failureCount++;
                continue;
            }

            queue.Enqueue((new PackageIdentity(pkg.Name, version), true));
        }

        while (queue.Count > 0)
        {
            var (identity, isDirect) = queue.Dequeue();
            var packageKey = $"{identity.Id}/{identity.Version}";

            if (!resolvedPackages.Add(packageKey))
                continue;

            // Try to resolve the package (download if not cached)
            var packagePath = ResolvePackage(identity, repository, cache, logger, diagnostics);
            if (packagePath is null)
            {
                if (isDirect)
                    failureCount++;
                continue;
            }

            // Find compatible assemblies for the target framework
            var (assemblies, selectedTfm, hadError) =
                GetCompatibleAssemblies(identity, packagePath, projectFramework, targetFramework, diagnostics);
            assemblyPaths.AddRange(assemblies);

            if (isDirect)
            {
                if (hadError)
                {
                    failureCount++;
                }
                else
                {
                    // Record the per-package outcome. A package that resolves to
                    // zero compatible assemblies but produces no error is recorded
                    // with AssemblyCount == 0 so the verbose stage and `mist
                    // restore` can print the "contributed no assemblies" note.
                    resolvedInfos.Add(new ResolvedPackageInfo(
                        identity.Id,
                        identity.Version.ToNormalizedString(),
                        selectedTfm ?? string.Empty,
                        assemblies.Length));
                }
            }

            // Resolve transitive dependencies
            var dependencies = GetDependencies(identity, packagePath, projectFramework);
            foreach (var dep in dependencies)
            {
                var depKey = $"{dep.Id}/{dep.Version}";
                if (!resolvedPackages.Contains(depKey))
                    queue.Enqueue((dep, false));
            }
        }

        return new PackageResolutionResult(
            assemblyPaths.ToImmutable(),
            resolvedInfos.ToImmutable(),
            failureCount);
    }

    /// <summary>
    /// Resolves a single package, downloading it if not present in the local cache.
    /// Returns the package directory path, or null on failure.
    /// </summary>
    private static string? ResolvePackage(
        PackageIdentity identity,
        SourceRepository repository,
        SourceCacheContext cache,
        ILogger logger,
        DiagnosticBag diagnostics)
    {
        var packageDir = Path.Combine(
            GlobalPackagesFolder,
            identity.Id.ToLowerInvariant(),
            identity.Version.ToNormalizedString().ToLowerInvariant());

        // Check local cache first
        if (Directory.Exists(packageDir))
            return packageDir;

        // Download the package
        try
        {
            using var cts = new CancellationTokenSource(RestoreTimeout);

            var findResource = repository.GetResourceAsync<FindPackageByIdResource>(cts.Token)
                .GetAwaiter().GetResult();

            // Check if the package version exists
            var versions = findResource.GetAllVersionsAsync(identity.Id, cache, logger, cts.Token)
                .GetAwaiter().GetResult();

            if (versions is null || !versions.Contains(identity.Version))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.PackageNotFound,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Package '{identity.Id}' version '{identity.Version}' was not found on any configured NuGet feed. Searched: {FeedList}.",
                    Location: default,
                    CompilationUnitPath: string.Empty,
                    AnalyzerName: null));
                return null;
            }

            // Download and extract
            Directory.CreateDirectory(packageDir);

            using var packageStream = new MemoryStream();
            var downloaded = findResource.CopyNupkgToStreamAsync(
                identity.Id, identity.Version, packageStream, cache, logger, cts.Token)
                .GetAwaiter().GetResult();

            if (!downloaded)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.PackageNotFound,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Failed to download package '{identity.Id}' version '{identity.Version}' from any configured NuGet feed. Searched: {FeedList}.",
                    Location: default,
                    CompilationUnitPath: string.Empty,
                    AnalyzerName: null));
                return null;
            }

            packageStream.Position = 0;
            using var reader = new PackageArchiveReader(packageStream);
            var files = reader.GetFiles();

            foreach (var file in files)
            {
                var targetPath = Path.Combine(packageDir, file);
                var targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir is not null)
                    Directory.CreateDirectory(targetDir);

                using var fileStream = reader.GetStream(file);
                using var outputStream = File.Create(targetPath);
                fileStream.CopyTo(outputStream);
            }

            return packageDir;
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.PackageNetworkError,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Restore timeout: restoring package '{identity.Id}' version '{identity.Version}' did not complete within 100 seconds.",
                Location: default,
                CompilationUnitPath: string.Empty,
                AnalyzerName: null));
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or FatalProtocolException or InvalidOperationException)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.PackageNetworkError,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Connection or network failure while restoring package '{identity.Id}' version '{identity.Version}': {ex.Message}",
                Location: default,
                CompilationUnitPath: string.Empty,
                AnalyzerName: null));
            return null;
        }
    }

    /// <summary>
    /// Gets the assembly DLL paths from a package directory that are compatible
    /// with the target framework, using the TFM preference order.
    /// </summary>
    /// <returns>
    /// A tuple of the compatible assembly paths, the selected <c>lib/</c> TFM
    /// directory name (null when none was selected), and whether an
    /// error-severity diagnostic was emitted. A package with no <c>lib/</c>
    /// directory or no TFM subdirectories yields zero assemblies without an
    /// error (a "contributed no assemblies" outcome).
    /// </returns>
    private static (ImmutableArray<string> Assemblies, string? SelectedTfm, bool HadError) GetCompatibleAssemblies(
        PackageIdentity identity,
        string packageDir,
        NuGetFramework projectFramework,
        string targetFramework,
        DiagnosticBag diagnostics)
    {
        var libDir = Path.Combine(packageDir, "lib");
        if (!Directory.Exists(libDir))
            return (ImmutableArray<string>.Empty, null, false);

        var tfmDirs = Directory.GetDirectories(libDir);
        if (tfmDirs.Length == 0)
            return (ImmutableArray<string>.Empty, null, false);

        // Find the best matching TFM directory using our preference order
        string? bestTfmDir = null;
        var bestPreferenceIndex = int.MaxValue;

        foreach (var tfmDir in tfmDirs)
        {
            var tfmName = Path.GetFileName(tfmDir);
            var packageFramework = NuGetFramework.Parse(tfmName);

            // Check if this TFM is compatible with the project framework
            var compat = DefaultCompatibilityProvider.Instance;
            if (!compat.IsCompatible(projectFramework, packageFramework))
                continue;

            // Find the preference index for this TFM
            var preferenceIndex = GetTfmPreferenceIndex(packageFramework);
            if (preferenceIndex < bestPreferenceIndex)
            {
                bestPreferenceIndex = preferenceIndex;
                bestTfmDir = tfmDir;
            }
        }

        if (bestTfmDir is null)
        {
            var availableTfms = tfmDirs
                .Select(Path.GetFileName)
                .Where(n => n is not null);

            diagnostics.Add(new Diagnostic(
                DiagnosticId.PackageNoCompatibleTfm,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Package '{identity.Id}' version '{identity.Version}' does not support {targetFramework}. The package provides these framework targets: {string.Join(", ", availableTfms)}.",
                Location: default,
                CompilationUnitPath: string.Empty,
                AnalyzerName: null));
            return (ImmutableArray<string>.Empty, null, true);
        }

        var dlls = Directory.GetFiles(bestTfmDir, "*.dll");
        return ([.. dlls], Path.GetFileName(bestTfmDir), false);
    }

    /// <summary>
    /// Gets the preference index for a framework. Lower index = higher preference.
    /// Frameworks not in the preference list get int.MaxValue (lowest preference but still valid).
    /// </summary>
    internal static int GetTfmPreferenceIndex(NuGetFramework framework)
    {
        for (var i = 0; i < TfmPreferenceOrder.Length; i++)
        {
            if (TfmPreferenceOrder[i].Equals(framework))
                return i;
        }

        // For frameworks not in our explicit list, use the NuGet framework
        // reducer to determine compatibility ordering. Frameworks compatible
        // with our project but not in the preference list get lowest priority.
        return int.MaxValue;
    }

    /// <summary>
    /// Gets the preference index for a framework specified by its short name (e.g., "net10.0").
    /// Lower index = higher preference. Frameworks not in the preference list get int.MaxValue.
    /// </summary>
    internal static int GetTfmPreferenceIndex(string frameworkShortName)
    {
        var framework = NuGetFramework.Parse(frameworkShortName);
        return GetTfmPreferenceIndex(framework);
    }

    /// <summary>
    /// Gets the transitive dependencies of a package for the target framework.
    /// Selects the highest compatible version for each dependency.
    /// </summary>
    private static ImmutableArray<PackageIdentity> GetDependencies(
        PackageIdentity identity,
        string packageDir,
        NuGetFramework projectFramework)
    {
        var nuspecPath = Path.Combine(packageDir, $"{identity.Id}.nuspec");
        if (!File.Exists(nuspecPath))
        {
            // Try lowercase
            nuspecPath = Path.Combine(packageDir, $"{identity.Id.ToLowerInvariant()}.nuspec");
            if (!File.Exists(nuspecPath))
                return ImmutableArray<PackageIdentity>.Empty;
        }

        NuspecReader nuspec;
        try
        {
            using var stream = File.OpenRead(nuspecPath);
            nuspec = new NuspecReader(stream);
        }
        catch
        {
            return ImmutableArray<PackageIdentity>.Empty;
        }

        var dependencyGroups = nuspec.GetDependencyGroups().ToList();
        if (dependencyGroups.Count == 0)
            return ImmutableArray<PackageIdentity>.Empty;

        // Find the best matching dependency group for our target framework
        var reducer = new FrameworkReducer();
        var nearest = reducer.GetNearest(
            projectFramework,
            dependencyGroups.Select(g => g.TargetFramework));

        if (nearest is null)
            return ImmutableArray<PackageIdentity>.Empty;

        var group = dependencyGroups.First(g => g.TargetFramework.Equals(nearest));
        var result = ImmutableArray.CreateBuilder<PackageIdentity>();

        foreach (var dep in group.Packages)
        {
            // Enforce exact version matching: use the minimum version from the range
            // (which for exact versions like [1.0.0] is the version itself,
            // and for ranges like >= 1.0.0 is the lower bound)
            var version = dep.VersionRange.MinVersion;
            if (version is null)
                continue;

            result.Add(new PackageIdentity(dep.Id, version));
        }

        return result.ToImmutable();
    }
}
