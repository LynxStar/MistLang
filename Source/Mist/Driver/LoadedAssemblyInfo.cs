using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Mist.Driver;

/// <summary>
/// Represents a loaded assembly's metadata for type resolution.
/// Built during assembly loading by the <c>AssemblyTypeProvider</c> to enable
/// fast namespace and type name lookups without re-scanning exported types.
/// </summary>
public sealed class LoadedAssemblyInfo
{
    /// <summary>
    /// The absolute file path of the assembly DLL on disk.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// The <see cref="Assembly"/> instance loaded via <c>MetadataLoadContext</c>
    /// for metadata-only inspection.
    /// </summary>
    public Assembly MetadataAssembly { get; }

    /// <summary>
    /// Pre-built index: namespace → type names in that namespace.
    /// Built during assembly loading by scanning <see cref="Assembly.ExportedTypes"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> NamespaceIndex { get; }

    /// <summary>
    /// Creates a new <see cref="LoadedAssemblyInfo"/> by scanning the assembly's
    /// exported types to build the namespace index.
    /// </summary>
    /// <param name="filePath">The absolute file path of the assembly DLL.</param>
    /// <param name="metadataAssembly">
    /// The assembly loaded via <c>MetadataLoadContext</c>.
    /// </param>
    public LoadedAssemblyInfo(string filePath, Assembly metadataAssembly)
    {
        FilePath = filePath;
        MetadataAssembly = metadataAssembly;
        NamespaceIndex = BuildNamespaceIndex(metadataAssembly);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildNamespaceIndex(
        Assembly assembly)
    {
        var index = new Dictionary<string, List<string>>();

        foreach (var type in assembly.ExportedTypes)
        {
            var ns = type.Namespace ?? string.Empty;

            if (!index.TryGetValue(ns, out var typeNames))
            {
                typeNames = new List<string>();
                index[ns] = typeNames;
            }

            typeNames.Add(type.Name);
        }

        // Convert to read-only collections for the public API.
        var result = new Dictionary<string, IReadOnlyList<string>>(index.Count);
        foreach (var (ns, typeNames) in index)
        {
            result[ns] = typeNames;
        }

        return result;
    }
}
