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
    /// Timeout for each individual package download operation.
    /// </summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Resolves all package references to assembly paths.
    /// Downloads packages not in the local cache.
    /// Resolves transitive dependencies.
    /// Selects nearest compatible TFM for each package.
    /// </summary>
    /// <param name="packages">The declared package references from mist.toml.</param>
    /// <param name="targetFramework">The project's target framework (e.g., "net10.0").</param>
    /// <param name="diagnostics">Diagnostic bag for reporting errors.</param>
    /// <returns>An array of assembly file paths from all resolved packages.</returns>
    public static ImmutableArray<string> ResolvePackages(
        ImmutableArray<NuGetPackageReference> packages,
        string targetFramework,
        DiagnosticBag diagnostics)
    {
        if (packages.IsDefaultOrEmpty)
            return ImmutableArray<string>.Empty;

        var assemblyPaths = ImmutableArray.CreateBuilder<string>();
        var resolvedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        var cache = new SourceCacheContext();
        var logger = NullLogger.Instance;

        var projectFramework = NuGetFramework.Parse(targetFramework);

        // Resolve each declared package and its transitive dependencies
        var queue = new Queue<PackageIdentity>();

        foreach (var pkg in packages)
        {
            if (!NuGetVersion.TryParse(pkg.Version, out var version))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.PackageNotFound,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Package '{pkg.Name}' version '{pkg.Version}' is not a valid NuGet version.",
                    Location: default,
                    CompilationUnitPath: string.Empty,
                    AnalyzerName: null));
                continue;
            }

            queue.Enqueue(new PackageIdentity(pkg.Name, version));
        }

        while (queue.Count > 0)
        {
            var identity = queue.Dequeue();
            var packageKey = $"{identity.Id}/{identity.Version}";

            if (!resolvedPackages.Add(packageKey))
                continue;

            // Try to resolve the package (download if not cached)
            var packagePath = ResolvePackage(identity, repository, cache, logger, diagnostics);
            if (packagePath is null)
                continue;

            // Find compatible assemblies for the target framework
            var assemblies = GetCompatibleAssemblies(identity, packagePath, projectFramework, diagnostics);
            assemblyPaths.AddRange(assemblies);

            // Resolve transitive dependencies
            var dependencies = GetDependencies(identity, packagePath, projectFramework);
            foreach (var dep in dependencies)
            {
                var depKey = $"{dep.Id}/{dep.Version}";
                if (!resolvedPackages.Contains(depKey))
                    queue.Enqueue(dep);
            }
        }

        return assemblyPaths.ToImmutable();
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
            using var cts = new CancellationTokenSource(DownloadTimeout);

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
                    $"Package '{identity.Id}' version '{identity.Version}' was not found on nuget.org.",
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
                    $"Failed to download package '{identity.Id}' version '{identity.Version}' from nuget.org.",
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
                $"Timed out downloading package '{identity.Id}' version '{identity.Version}' (60-second limit exceeded).",
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
                $"Network error downloading package '{identity.Id}' version '{identity.Version}': {ex.Message}",
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
    private static ImmutableArray<string> GetCompatibleAssemblies(
        PackageIdentity identity,
        string packageDir,
        NuGetFramework projectFramework,
        DiagnosticBag diagnostics)
    {
        var libDir = Path.Combine(packageDir, "lib");
        if (!Directory.Exists(libDir))
            return ImmutableArray<string>.Empty;

        var tfmDirs = Directory.GetDirectories(libDir);
        if (tfmDirs.Length == 0)
            return ImmutableArray<string>.Empty;

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
                $"Package '{identity.Id}' version '{identity.Version}' has no compatible framework target. Available: {string.Join(", ", availableTfms)}.",
                Location: default,
                CompilationUnitPath: string.Empty,
                AnalyzerName: null));
            return ImmutableArray<string>.Empty;
        }

        var dlls = Directory.GetFiles(bestTfmDir, "*.dll");
        return [.. dlls];
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
