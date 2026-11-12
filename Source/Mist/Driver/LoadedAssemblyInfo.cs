using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Mist.Driver;

/// <summary>
/// Indexes exported types by namespace once, so type resolution never rescans them.
/// </summary>
public sealed class LoadedAssemblyInfo
{
    public string FilePath { get; }

    /// <summary>
    /// Metadata only: loaded through a <c>MetadataLoadContext</c>.
    /// </summary>
    public Assembly MetadataAssembly { get; }

    /// <summary>
    /// Namespace to the names of its exported types.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> NamespaceIndex { get; }

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

        foreach(var type in assembly.ExportedTypes)
        {
            var ns = type.Namespace ?? string.Empty;

            if(!index.TryGetValue(ns, out var typeNames))
            {
                typeNames = new List<string>();
                index[ns] = typeNames;
            }

            typeNames.Add(type.Name);
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(index.Count);
        foreach(var (ns, typeNames) in index)
        {
            result[ns] = typeNames;
        }

        return result;
    }
}