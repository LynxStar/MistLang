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

    IReadOnlyDictionary<string, IMistType> INamespace.Types => Types;
}