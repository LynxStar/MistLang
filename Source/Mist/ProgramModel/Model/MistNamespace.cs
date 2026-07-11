using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Concrete implementation of <see cref="INamespace"/>.
/// Populated by the builder during construction.
/// </summary>
public sealed class MistNamespace(string fullyQualifiedName) : INamespace
{
    public string FullyQualifiedName { get; } = fullyQualifiedName;

    public Dictionary<string, IMistType> Types { get; } = [];

    /// <summary>
    /// Extension blocks declared in this namespace, grouped by resolved receiver type.
    /// Populated during Phase 2 when extension receiver types are resolved.
    /// </summary>
    internal Dictionary<IMistType, List<ExtensionBlock>> Extensions { get; } = [];

    IReadOnlyDictionary<string, IMistType> INamespace.Types => Types;

    IReadOnlyDictionary<IMistType, IReadOnlyList<IExtensionBlock>> INamespace.Extensions =>
        Extensions.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<IExtensionBlock>)kvp.Value);
}