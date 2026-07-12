using System.Collections.Immutable;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Construction;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Factory for creating <see cref="AmbientTypeProviderV2"/> instances.
/// Handles SDK discovery, assembly loading, and error reporting.
/// </summary>
internal static class AmbientTypeProviderV2Factory
{
    /// <summary>
    /// Creates an AmbientTypeProviderV2 by discovering SDK reference assemblies for the
    /// given target framework and merging them with additional assembly paths (NuGet packages,
    /// framework references, etc.). Uses <see cref="MetadataLoadContext"/> for reflection-only loading.
    /// Assemblies that fail to load emit a diagnostic and are skipped.
    /// </summary>
    /// <param name="targetFramework">The target framework moniker (e.g., "net9.0").</param>
    /// <param name="additionalAssemblyPaths">Additional assembly paths from NuGet/framework resolution.</param>
    /// <param name="typeInterner">The type interner for creating constructed types.</param>
    /// <param name="diagnostics">Diagnostic bag for reporting assembly load failures.</param>
    /// <returns>A fully initialized <see cref="AmbientTypeProviderV2"/> instance.</returns>
    public static AmbientTypeProviderV2 Create(
        string targetFramework,
        ImmutableArray<string> additionalAssemblyPaths,
        TypeInterner typeInterner,
        DiagnosticBag diagnostics)
    {
        var assemblyPaths = ResolveSdkAndAdditionalPaths(targetFramework, additionalAssemblyPaths, diagnostics);

        var resolver = new PathAssemblyResolver(assemblyPaths);
        var context = new MetadataLoadContext(resolver);
        var loaded = LoadAssemblies(assemblyPaths, context, diagnostics);

        return new AmbientTypeProviderV2(loaded, typeInterner, context, diagnostics);
    }

    /// <summary>
    /// Resolves the full set of assembly paths needed by the ambient provider:
    /// SDK reference assemblies (System.Runtime, etc.) merged with any NuGet/framework
    /// assembly paths already resolved by the driver.
    /// </summary>
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

    /// <summary>
    /// Loads all assemblies from the given paths via <see cref="MetadataLoadContext"/>.
    /// Assemblies that fail to load emit a diagnostic and are skipped.
    /// </summary>
    internal static ImmutableArray<LoadedAssemblyInfo> LoadAssemblies(
        ImmutableArray<string> assemblyPaths,
        MetadataLoadContext context,
        DiagnosticBag diagnostics)
    {
        var loaded = ImmutableArray.CreateBuilder<LoadedAssemblyInfo>();

        foreach(var path in assemblyPaths)
        {
            try
            {
                var assembly = context.LoadFromAssemblyPath(path);
                var info = new LoadedAssemblyInfo(path, assembly);
                loaded.Add(info);
            }
            catch(Exception ex)
            {
                diagnostics.Report(
                    BackendDiagnostics.AssemblyLoadFailed,
                    default,
                    "<assembly>",
                    (object?)path,
                    ex.Message);
            }
        }

        return loaded.ToImmutable();
    }
}
