using System.Collections.Immutable;
using Mist.Driver.Contracts;
using NuGet.Versioning;

namespace Mist.Driver;

/// <summary>
/// Every project in a compilation lands in one assembly, so a dependency a referenced manifest
/// declares is the root's as well. A declared version is a floor, as it is for NuGet, so where two
/// manifests name one package the highest version declared is the lowest that satisfies both.
/// </summary>
internal static class ProjectDependencyUnion
{
    public static CompilerOptions Apply(
        CompilerOptions options,
        ImmutableArray<ProjectConfiguration> referencedProjects)
    {
        if(referencedProjects.IsDefaultOrEmpty)
        {
            return options;
        }

        var packages = referencedProjects.Select(p => p.Packages).Prepend(options.Packages);
        var frameworks = referencedProjects.Select(p => p.FrameworkReferences).Prepend(options.FrameworkReferences);

        return options with
        {
            Packages = UnionPackages(packages),
            FrameworkReferences = UnionFrameworkReferences(frameworks),
        };
    }

    private static ImmutableArray<NuGetPackageReference> UnionPackages(
        IEnumerable<ImmutableArray<NuGetPackageReference>> declarations)
    {
        var chosen = new Dictionary<string, NuGetPackageReference>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach(var declared in Flatten(declarations))
        {
            if(!chosen.TryGetValue(declared.Name, out var existing))
            {
                chosen[declared.Name] = declared;
                order.Add(declared.Name);
            }
            else if(IsHigher(declared.Version, existing.Version))
            {
                chosen[declared.Name] = declared;
            }
        }

        return order.Select(name => chosen[name]).ToImmutableArray();
    }

    private static ImmutableArray<FrameworkReference> UnionFrameworkReferences(
        IEnumerable<ImmutableArray<FrameworkReference>> declarations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var union = ImmutableArray.CreateBuilder<FrameworkReference>();

        foreach(var framework in Flatten(declarations))
        {
            if(seen.Add(framework.Name))
            {
                union.Add(framework);
            }
        }

        return union.ToImmutable();
    }

    private static IEnumerable<T> Flatten<T>(IEnumerable<ImmutableArray<T>> declarations)
    {
        return declarations.Where(d => !d.IsDefault).SelectMany(d => d);
    }

    private static bool IsHigher(string candidate, string existing)
    {
        return NuGetVersion.TryParse(candidate, out var candidateVersion)
            && NuGetVersion.TryParse(existing, out var existingVersion)
            && candidateVersion > existingVersion;
    }
}