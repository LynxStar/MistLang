using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Holds the declarations active only where the namespace is imported; types live in the flat
/// <see cref="ProgramModel.TypesByFQN"/> instead.
/// </summary>
public sealed class MistNamespace(string fullyQualifiedName) : INamespace
{
    public string FullyQualifiedName { get; } = fullyQualifiedName;

    /// <summary>
    /// Keyed by resolved receiver type, so filled in Phase 2.
    /// </summary>
    internal Dictionary<IMistType, List<ExtensionBlock>> Extensions { get; } = [];

    IReadOnlyDictionary<IMistType, IReadOnlyList<IExtensionBlock>> INamespace.Extensions =>
        Extensions.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<IExtensionBlock>)kvp.Value);
}