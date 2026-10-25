using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler;

internal static class RuntimeAssetDeployment
{
    public static DeploymentPlan Plan(
        string binDirectory,
        ImmutableArray<string> referencePaths,
        ImmutableArray<RuntimeAsset> runtimeAssets,
        ImmutableArray<ExternalAssembly> externalAssemblies,
        ImmutableArray<string> runtimeIdentifiers,
        IDeploymentFileSource files)
    {
        var claims = new Dictionary<string, Claim>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();

        foreach(var path in referencePaths.IsDefault ? [] : referencePaths)
        {
            offer(path, kind: null);
        }

        foreach(var asset in Assets(runtimeAssets, externalAssemblies, runtimeIdentifiers, files))
        {
            offer(asset.Path, asset.Kind);
        }

        var copies = names
            .Select(name => (Claim: claims[name], Destination: Path.Combine(binDirectory, name)))
            .Where(entry => entry.Claim.Replaces || Stale(entry.Claim.Source, entry.Destination, files))
            .Select(entry => new FileCopy(entry.Claim.Source, entry.Destination));

        return new DeploymentPlan([.. copies], claims.Values.Count(claim => claim.IsAsset));

        void offer(string source, RuntimeAssetKind? kind)
        {
            if(!files.Exists(source))
            {
                return;
            }

            var name = Path.GetFileName(source);
            if(!claims.TryGetValue(name, out var existing))
            {
                claims[name] = new Claim(source, IsAsset: kind is not null, Replaces: false);
                names.Add(name);
            }
            else if(kind == RuntimeAssetKind.Managed && !existing.IsAsset)
            {
                claims[name] = new Claim(source, IsAsset: true, Replaces: true);
            }
        }
    }

    private static IEnumerable<RuntimeAsset> Assets(
        ImmutableArray<RuntimeAsset> runtimeAssets,
        ImmutableArray<ExternalAssembly> externalAssemblies,
        ImmutableArray<string> runtimeIdentifiers,
        IDeploymentFileSource files)
    {
        var packaged = runtimeAssets.IsDefault ? [] : runtimeAssets;
        var projects = externalAssemblies.IsDefault ? [] : externalAssemblies;
        var closure = projects
            .Where(external => external.Kind == ReferenceKind.CSharpProject)
            .Select(external => Path.GetDirectoryName(external.Path))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .SelectMany(directory => ClosureAssetSelection
                .Select(files.RuntimesListing(directory), runtimeIdentifiers)
                .Select(asset => asset with { Path = Path.Combine(directory, asset.Path) }));

        return packaged.Concat(closure);
    }

    private static bool Stale(string source, string destination, IDeploymentFileSource files)
    {
        return !files.Exists(destination) || files.LastWriteTimeUtc(source) > files.LastWriteTimeUtc(destination);
    }

    /// <summary>
    /// <see cref="Replaces"/> copies whatever the timestamps say: the file already in bin may be the package's
    /// <c>lib/</c> copy, newer than the rid-specific source, and the refresh rule alone would keep it.
    /// </summary>
    private sealed record Claim(string Source, bool IsAsset, bool Replaces);
}