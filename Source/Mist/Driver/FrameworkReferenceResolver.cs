using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Resolves enabled <c>[framework_references]</c> entries to their installed
/// shared-framework reference-assembly directories.
/// </summary>
/// <remarks>
/// <para>
/// The reference pack path mirrors the SDK ref pattern used by
/// <see cref="SdkDiscovery"/>:
/// <c>{dotnetRoot}/packs/{FrameworkName}.Ref/{version}/ref/{tfm}/</c>. The
/// dotnet root is discovered via <see cref="SdkDiscovery.FindDotnetRoot"/> so
/// both the SDK reference assemblies and framework reference packs are located
/// relative to the same installation.
/// </para>
/// <para>
/// Resolution of a framework is treated as successful only when the pack
/// directory exists and contains at least one reference assembly. When multiple
/// pack versions are installed, the resolver selects, among the packs whose
/// major version matches the Target_Framework's major version, the pack with the
/// greatest version by integer major→minor→patch comparison.
/// </para>
/// </remarks>
public static class FrameworkReferenceResolver
{
    /// <summary>
    /// The set of shared-framework names Mist recognizes. An enabled framework
    /// whose name is absent from this allow-list is reported as an unknown
    /// shared framework (<c>MIST0035</c>) without any attempt to locate a
    /// reference pack for it; a name that is present but whose pack cannot be
    /// located is reported as not-installed (<c>MIST0034</c>).
    /// </summary>
    private static readonly ImmutableHashSet<string> KnownFrameworks =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Microsoft.AspNetCore.App",
            "Microsoft.WindowsDesktop.App",
            "Microsoft.NETCore.App");

    /// <summary>
    /// Resolves each enabled framework reference independently to its reference
    /// assemblies for the given target framework.
    /// </summary>
    /// <param name="frameworks">The enabled framework references from mist.toml.</param>
    /// <param name="targetFramework">The project's target framework (e.g., "net10.0").</param>
    /// <param name="diagnostics">Diagnostic bag for reporting resolution failures.</param>
    /// <returns>
    /// A <see cref="FrameworkResolutionResult"/> carrying the flattened
    /// reference-assembly paths, a per-resolved-framework summary, and the count
    /// of enabled frameworks that failed to resolve.
    /// </returns>
    public static FrameworkResolutionResult Resolve(
        ImmutableArray<FrameworkReference> frameworks,
        string targetFramework,
        DiagnosticBag diagnostics)
    {
        if(frameworks.IsDefaultOrEmpty)
        {
            return new FrameworkResolutionResult(
                ImmutableArray<string>.Empty,
                ImmutableArray<ResolvedFrameworkInfo>.Empty,
                FailureCount: 0);
        }

        var installation = new Lazy<string?>(SdkDiscovery.FindDotnetRoot);

        var assemblyPaths = ImmutableArray.CreateBuilder<string>();
        var resolvedInfos = ImmutableArray.CreateBuilder<ResolvedFrameworkInfo>();
        var failureCount = 0;

        // Each enabled framework resolves independently; a failure to resolve one
        // framework never short-circuits the remaining frameworks. Exactly one
        // diagnostic is emitted per failing framework.
        foreach(var framework in frameworks)
        {
            // Unknown shared-framework name: report MIST0035 and do not
            // attempt to locate a pack (we have no canonical pack name to search).
            if(!KnownFrameworks.Contains(framework.Name))
            {
                diagnostics.Add(DiagnosticFactory.Legacy(
                    CommonDiagnostics.FrameworkReferenceUnknownName,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Framework reference '{framework.Name}' is not a known shared framework.",
                    default,
                    string.Empty));
                failureCount++;
                continue;
            }

            var dotnetRoot = installation.Value;
            var resolved = dotnetRoot is null
                ? null
                : ResolvePack(dotnetRoot, framework.Name, targetFramework);

            if(resolved is null)
            {
                // Known shared framework, but its reference pack is not installed
                // for the target framework: report MIST0034, naming the
                // framework, the target framework, and the pack path searched.
                diagnostics.Add(DiagnosticFactory.Legacy(
                    CommonDiagnostics.FrameworkReferenceNotInstalled,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Framework reference '{framework.Name}' is not installed for {targetFramework}. Searched: {DescribeSearchedPath(dotnetRoot, framework.Name, targetFramework)}.",
                    default,
                    string.Empty));
                failureCount++;
                continue;
            }

            assemblyPaths.AddRange(resolved.Value.Assemblies);
            resolvedInfos.Add(new ResolvedFrameworkInfo(framework.Name, resolved.Value.Version));
        }

        return new FrameworkResolutionResult(
            assemblyPaths.ToImmutable(),
            resolvedInfos.ToImmutable(),
            failureCount);
    }

    /// <summary>
    /// Locates the highest-version reference pack for the given framework that
    /// supports the target framework. Path pattern:
    /// <c>{dotnetRoot}/packs/{frameworkName}.Ref/{version}/ref/{tfm}/</c>.
    /// Resolution succeeds only when the <c>ref/{tfm}</c> directory exists and
    /// contains at least one reference assembly. Among packs whose major version
    /// matches the TFM major, the greatest by integer major→minor→patch
    /// comparison is selected.
    /// </summary>
    private static (ImmutableArray<string> Assemblies, string Version)? ResolvePack(
        string dotnetRoot,
        string frameworkName,
        string targetFramework)
    {
        var packsDir = Path.Combine(dotnetRoot, "packs", $"{frameworkName}.Ref");
        if(!Directory.Exists(packsDir))
        {
            return null;
        }

        string[] versionDirs;
        try
        {
            versionDirs = Directory.GetDirectories(packsDir);
        }
        catch
        {
            return null;
        }

        // Only packs whose major version matches the TFM major are candidates.
        var tfmMajor = SdkDiscovery.ExtractTfmMajorVersion(targetFramework);

        Version? highestVersion = null;
        string? highestVersionName = null;
        ImmutableArray<string> highestAssemblies = default;

        foreach(var versionDir in versionDirs)
        {
            var dirName = Path.GetFileName(versionDir);
            if(!Version.TryParse(dirName, out var version))
            {
                continue;
            }

            if(tfmMajor.HasValue && version.Major != tfmMajor.Value)
            {
                continue;
            }

            var refPath = Path.Combine(versionDir, "ref", targetFramework);
            if(!Directory.Exists(refPath))
            {
                continue;
            }

            // Success requires at least one reference assembly in the pack.
            var dlls = Directory.GetFiles(refPath, "*.dll");
            if(dlls.Length == 0)
            {
                continue;
            }

            if(highestVersion is null || version > highestVersion)
            {
                highestVersion = version;
                highestVersionName = dirName;
                highestAssemblies = [.. dlls];
            }
        }

        if(highestVersionName is null)
        {
            return null;
        }

        return (highestAssemblies, highestVersionName);
    }

    /// <summary>
    /// Renders the reference-pack location that was searched, for inclusion in
    /// failure diagnostics.
    /// </summary>
    private static string DescribeSearchedPath(string? dotnetRoot, string frameworkName, string targetFramework)
    {
        var packFragment = Path.Combine($"{frameworkName}.Ref", "{version}", "ref", targetFramework);
        return dotnetRoot is null
            ? Path.Combine("packs", packFragment)
            : Path.Combine(dotnetRoot, "packs", packFragment);
    }
}