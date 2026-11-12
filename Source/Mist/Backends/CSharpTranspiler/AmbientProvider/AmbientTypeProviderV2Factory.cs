using System.Collections.Immutable;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class AmbientTypeProviderV2Factory
{
    /// <param name="unregisteredAssemblyNames">
    /// Simple names loaded for resolution and never registered: the composed Mist projects, whose standalone
    /// copies would otherwise shadow or duplicate the declared types.
    /// </param>
    public static AmbientTypeProviderV2 Create(
        string targetFramework,
        ImmutableArray<string> additionalAssemblyPaths,
        DiagnosticBag diagnostics,
        ImmutableArray<string> resolverOnlyDirectories = default,
        IEnumerable<string>? unregisteredAssemblyNames = null)
    {
        var assemblyPaths = ResolveSdkAndAdditionalPaths(targetFramework, additionalAssemblyPaths, diagnostics);

        return CreateOver(assemblyPaths, resolverOnlyDirectories, unregisteredAssemblyNames, diagnostics);
    }

    internal static AmbientTypeProviderV2 CreateOver(
        ImmutableArray<string> assemblyPaths,
        ImmutableArray<string> resolverOnlyDirectories,
        IEnumerable<string>? unregisteredAssemblyNames,
        DiagnosticBag diagnostics)
    {
        var resolvable = resolverOnlyDirectories.IsDefaultOrEmpty
            ? assemblyPaths
            : [.. assemblyPaths
                .Concat(resolverOnlyDirectories.Where(Directory.Exists).SelectMany(directory => Directory.GetFiles(directory, "*.dll")))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

        var resolver = new PathAssemblyResolver(resolvable);
        var context = new MetadataLoadContext(resolver);
        var loaded = LoadAssemblies(assemblyPaths, context, diagnostics, unregisteredAssemblyNames);

        return new AmbientTypeProviderV2(loaded, context, diagnostics);
    }

    private static ImmutableArray<string> ResolveSdkAndAdditionalPaths(
        string targetFramework,
        ImmutableArray<string> additionalAssemblyPaths,
        DiagnosticBag diagnostics)
    {
        var sdkRefPath = SdkDiscovery.FindReferenceAssemblyPath(targetFramework, diagnostics);

        if(sdkRefPath is null)
        {
            return additionalAssemblyPaths.IsDefaultOrEmpty
                ? ImmutableArray<string>.Empty
                : additionalAssemblyPaths;
        }

        var sdkPaths = Directory.GetFiles(sdkRefPath, "*.dll");
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach(var path in sdkPaths)
        {
            _ = merged.Add(path);
        }

        if(!additionalAssemblyPaths.IsDefaultOrEmpty)
        {
            foreach(var path in additionalAssemblyPaths)
            {
                _ = merged.Add(path);
            }
        }

        return merged.ToImmutableArray();
    }

    internal static ImmutableArray<LoadedAssemblyInfo> LoadAssemblies(
        ImmutableArray<string> assemblyPaths,
        MetadataLoadContext context,
        DiagnosticBag diagnostics,
        IEnumerable<string>? unregisteredAssemblyNames = null)
    {
        var loaded = ImmutableArray.CreateBuilder<LoadedAssemblyInfo>();
        var unregistered = (unregisteredAssemblyNames ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach(var path in assemblyPaths)
        {
            try
            {
                var assembly = context.LoadFromAssemblyPath(path);
                if(assembly.GetName().Name is { } name && unregistered.Contains(name))
                {
                    continue;
                }

                var info = new LoadedAssemblyInfo(path, assembly);
                loaded.Add(info);
            }
            catch(Exception ex)
            {
                diagnostics.ReportTyped(
                    BackendDiagnostics.AssemblyLoadFailed,
                    new FileOnly(path),
                    new PathValue(path),
                    new TextValue(ex.Message));
            }
        }

        return loaded.ToImmutable();
    }
}