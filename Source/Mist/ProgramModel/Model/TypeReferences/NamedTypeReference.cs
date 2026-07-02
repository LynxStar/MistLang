using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// A reference to a named type (class, struct, abstract, enum, newtype, type alias).
/// </summary>
internal sealed class NamedTypeReference(ITypeDefinition definition) : INamedTypeReference
{
    public ITypeDefinition Definition { get; } = definition;

    public string DisplayName { get; } = definition.Name;
}