using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Concrete implementation of <see cref="IProgramModel"/>.
/// Populated by the builder, then exposed as read-only through the interface.
/// </summary>
internal sealed class ProgramModel : IProgramModel
{
    private readonly Dictionary<string, NamespaceDefinition> namespaces = [];

    /// <summary>
    /// All registered namespaces, keyed by fully qualified name.
    /// </summary>
    public IReadOnlyDictionary<string, NamespaceDefinition> Namespaces => namespaces;

    /// <summary>
    /// Gets an existing namespace or creates and registers a new one.
    /// Multiple files may declare the same namespace, so this deduplicates.
    /// </summary>
    public NamespaceDefinition GetOrAddNamespace(string fullyQualifiedName)
    {
        if(namespaces.TryGetValue(fullyQualifiedName, out var existing))
        {
            return existing;
        }

        var newNamespace = new NamespaceDefinition(fullyQualifiedName);
        namespaces[fullyQualifiedName] = newNamespace;
        return newNamespace;
    }
}