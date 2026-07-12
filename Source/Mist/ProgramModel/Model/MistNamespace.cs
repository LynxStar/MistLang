using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Concrete implementation of <see cref="INamespace"/>.
/// Holds namespace-scoped declarations (extensions, conformances) that are
/// only active when the namespace is imported. Types are stored in the
/// flat <see cref="ProgramModel.TypesByFQN"/> dictionary instead.
/// </summary>
public sealed class MistNamespace(string fullyQualifiedName) : INamespace
{
    public string FullyQualifiedName { get; } = fullyQualifiedName;

    /// <summary>
    /// Extension blocks declared in this namespace, grouped by resolved receiver type.
    /// Populated during Phase 2 when extension receiver types are resolved.
    /// </summary>
    internal Dictionary<IMistType, List<ExtensionBlock>> Extensions { get; } = [];

    IReadOnlyDictionary<IMistType, IReadOnlyList<IExtensionBlock>> INamespace.Extensions =>
        Extensions.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<IExtensionBlock>)kvp.Value);
}