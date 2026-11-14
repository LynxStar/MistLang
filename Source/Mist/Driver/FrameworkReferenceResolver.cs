using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Packs are found under the same dotnet root as <see cref="SdkDiscovery"/>'s, so the SDK reference
/// assemblies and the framework packs come from one installation.
/// </summary>
public static class FrameworkReferenceResolver
{
    private static readonly ImmutableHashSet<string> KnownFrameworks =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Microsoft.AspNetCore.App",
            "Microsoft.WindowsDesktop.App",
            "Microsoft.NETCore.App");

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

        // Exactly one diagnostic per failing framework, and a failure never stops the rest.
        foreach(var framework in frameworks)
        {
            // An unknown name has no canonical pack to search for.
            if(!KnownFrameworks.Contains(framework.Name))
            {
                diagnostics.ReportTyped(CommonDiagnostics.FrameworkReferenceUnknownName, new Absent(AbsenceReason.Unavailable),
                    new NameValue(framework.Name));
                failureCount++;
                continue;
            }

            var dotnetRoot = installation.Value;
            var resolved = dotnetRoot is null
                ? null
                : ResolvePack(dotnetRoot, framework.Name, targetFramework);

            if(resolved is null)
            {
                diagnostics.ReportTyped(CommonDiagnostics.FrameworkReferenceNotInstalled, new Absent(AbsenceReason.Unavailable),
                    new NameValue(framework.Name), new NameValue(targetFramework),
                    new PathValue(DescribeSearchedPath(dotnetRoot, framework.Name, targetFramework)));
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

    private static string DescribeSearchedPath(string? dotnetRoot, string frameworkName, string targetFramework)
    {
        var packFragment = Path.Combine($"{frameworkName}.Ref", "{version}", "ref", targetFramework);
        return dotnetRoot is null
            ? Path.Combine("packs", packFragment)
            : Path.Combine(dotnetRoot, "packs", packFragment);
    }
}