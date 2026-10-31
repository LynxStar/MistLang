using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler;

internal static class StaticAssetDeployment
{
    public const string FrameworkAssetsPack = "Microsoft.AspNetCore.App.Internal.Assets";

    public const string AspNetCoreFramework = "Microsoft.AspNetCore.App";

    private const string ComponentExtension = ".razor";

    private const string WebRoot = "wwwroot";

    private const string FrameworkFolder = "_framework";

    public static bool ServesFrameworkAssets(
        ProjectOutputType outputType, ImmutableArray<FrameworkReference> frameworkReferences, Func<bool> hasComponentFiles)
    {
        return outputType == ProjectOutputType.Executable
            && !frameworkReferences.IsDefault
            && frameworkReferences.Any(reference => reference.Name == AspNetCoreFramework)
            && hasComponentFiles();
    }

    public static ImmutableArray<NuGetPackageReference> WithFrameworkAssetsPack(
        ImmutableArray<NuGetPackageReference> packages, bool servesFrameworkAssets, Func<string?> targetingPackVersion)
    {
        var declared = packages.IsDefault ? [] : packages;
        if(!servesFrameworkAssets
            || declared.Any(package => string.Equals(package.Name, FrameworkAssetsPack, StringComparison.OrdinalIgnoreCase))
            || targetingPackVersion() is not { } version)
        {
            return packages;
        }

        return declared.Add(new NuGetPackageReference(FrameworkAssetsPack, version));
    }

    public static bool HasComponentFiles(ImmutableArray<ClaimedInput> claimedInputs, string projectFolder)
    {
        return !string.IsNullOrEmpty(projectFolder) && claimedInputs.Any(input =>
            string.Equals(Path.GetExtension(input.Text.Path), ComponentExtension, StringComparison.OrdinalIgnoreCase)
            && Path.GetFullPath(input.Project.ProjectFolder) == Path.GetFullPath(projectFolder));
    }

    public static string? FrameworkAssetsDirectory(bool servesFrameworkAssets, PackageResolutionResult packages)
    {
        return servesFrameworkAssets ? packages.PackageDirectories.GetValueOrDefault(FrameworkAssetsPack) : null;
    }

    public static ImmutableArray<FileCopy> Plan(
        string binDirectory, string projectFolder, string? frameworkAssetsDirectory, IDeploymentFileSource files)
    {
        var webRoot = Path.Combine(projectFolder, WebRoot);
        var selected = Select(
            binDirectory,
            webRoot,
            string.IsNullOrEmpty(projectFolder) ? [] : files.Listing(webRoot),
            frameworkAssetsDirectory,
            frameworkAssetsDirectory is null ? [] : files.Listing(frameworkAssetsDirectory));
        return [.. selected.Where(copy => RuntimeAssetDeployment.Stale(copy.Source, copy.Destination, files))];
    }

    public static ImmutableArray<FileCopy> Select(
        string binDirectory,
        string webRoot,
        ImmutableArray<string> webRootListing,
        string? packageDirectory,
        ImmutableArray<string> packageListing)
    {
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copies = ImmutableArray.CreateBuilder<FileCopy>();

        foreach(var relative in webRootListing)
        {
            offer(Path.Combine(webRoot, relative), Segments(relative));
        }

        foreach(var relative in packageListing)
        {
            var segments = Segments(relative);
            if(packageDirectory is not null && segments.Length > 1 && segments[0] == FrameworkFolder)
            {
                offer(Path.Combine(packageDirectory, relative), segments);
            }
        }

        return copies.ToImmutable();

        void offer(string source, string[] segments)
        {
            var destination = Path.Combine([binDirectory, WebRoot, .. segments]);
            if(destinations.Add(destination))
            {
                copies.Add(new FileCopy(source, destination));
            }
        }
    }

    private static string[] Segments(string relative)
    {
        return relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
    }
}