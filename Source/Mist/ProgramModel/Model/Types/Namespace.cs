using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Concrete implementation of <see cref="INamespace"/>.
/// Populated by the builder during construction.
/// </summary>
internal sealed class Namespace(string fullyQualifiedName) : INamespace
{
    public string FullyQualifiedName { get; } = fullyQualifiedName;

    public Dictionary<string, IMistType> Types { get; } = [];

    /// <summary>
    /// Extension blocks declared in this namespace, grouped by resolved receiver type.
    /// Populated during Phase 2 when extension receiver types are resolved.
    /// </summary>
    public Dictionary<IMistType, List<ExtensionBlock>> Extensions { get; } = [];

    IReadOnlyDictionary<string, IMistType> INamespace.Types => Types;

    IReadOnlyDictionary<IMistType, IReadOnlyList<IExtensionBlock>> INamespace.Extensions =>
        Extensions.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<IExtensionBlock>)kvp.Value);
}