using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Concrete implementation of <see cref="INamespaceDefinition"/>.
/// Populated by the builder during construction.
/// </summary>
internal sealed class NamespaceDefinition(string fullyQualifiedName) : INamespaceDefinition
{
    public string FullyQualifiedName { get; } = fullyQualifiedName;

    public Dictionary<string, ITypeDefinition> Types { get; } = [];

    IReadOnlyDictionary<string, ITypeDefinition> INamespaceDefinition.Types => Types;
}