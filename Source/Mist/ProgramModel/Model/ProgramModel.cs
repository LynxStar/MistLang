using System.Diagnostics.CodeAnalysis;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Concrete implementation of <see cref="IProgramModel"/>.
/// Populated by the builder, then exposed as read-only through the interface.
/// </summary>
// Suppressed: the class name matches the feature namespace by design.
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Intentional - the class represents the concept that the namespace is named after")]
public sealed class ProgramModel : IProgramModel
{
    private readonly Dictionary<string, MistNamespace> namespaces = [];

    /// <summary>
    /// All registered namespaces, keyed by fully qualified name.
    /// </summary>
    public IReadOnlyDictionary<string, MistNamespace> Namespaces => namespaces;

    /// <summary>
    /// Interns synthetic types (unions, constructed types) so that structurally
    /// identical type references share a single instance throughout the model's lifetime.
    /// </summary>
    internal TypeInterner TypeInterner { get; } = new();

    /// <summary>
    /// Retroactive conformance declarations collected during Phase 2c.
    /// </summary>
    internal List<ConformanceDeclaration> ConformancesList { get; } = [];

    IReadOnlyList<IConformanceDeclaration> IProgramModel.Conformances => ConformancesList;

    /// <summary>
    /// Gets an existing namespace or creates and registers a new one.
    /// Multiple files may declare the same namespace, so this deduplicates.
    /// </summary>
    public MistNamespace GetOrAddNamespace(string fullyQualifiedName)
    {
        if(namespaces.TryGetValue(fullyQualifiedName, out var existing))
        {
            return existing;
        }

        var newNamespace = new MistNamespace(fullyQualifiedName);
        namespaces[fullyQualifiedName] = newNamespace;
        return newNamespace;
    }
}