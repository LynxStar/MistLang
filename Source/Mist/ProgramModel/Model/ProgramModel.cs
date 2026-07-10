using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Concrete implementation of <see cref="IProgramModel"/>.
/// Populated by the builder, then exposed as read-only through the interface.
/// </summary>
internal sealed class ProgramModel : IProgramModel
{
    private readonly Dictionary<string, Namespace> namespaces = [];

    /// <summary>
    /// All registered namespaces, keyed by fully qualified name.
    /// </summary>
    public IReadOnlyDictionary<string, Namespace> Namespaces => namespaces;

    /// <summary>
    /// Interns synthetic types (unions, constructed types) so that structurally
    /// identical type references share a single instance throughout the model's lifetime.
    /// </summary>
    public TypeInterner TypeInterner { get; } = new();

    /// <summary>
    /// Retroactive conformance declarations collected during Phase 2c.
    /// </summary>
    public List<ConformanceDeclaration> ConformancesList { get; } = [];

    IReadOnlyList<IConformanceDeclaration> IProgramModel.Conformances => ConformancesList;

    /// <summary>
    /// Gets an existing namespace or creates and registers a new one.
    /// Multiple files may declare the same namespace, so this deduplicates.
    /// </summary>
    public Namespace GetOrAddNamespace(string fullyQualifiedName)
    {
        if(namespaces.TryGetValue(fullyQualifiedName, out var existing))
        {
            return existing;
        }

        var newNamespace = new Namespace(fullyQualifiedName);
        namespaces[fullyQualifiedName] = newNamespace;
        return newNamespace;
    }
}