using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// A reference to a named type (class, struct, abstract, enum, newtype, type alias).
/// </summary>
internal sealed class NamedTypeReference(IMistType definition) : INamedTypeReference
{
    public IMistType Definition { get; } = definition;

    public string DisplayName { get; } = definition.Name;
}