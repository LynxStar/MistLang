using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// An abstract (interface-like) type definition in the program model.
/// </summary>
internal sealed class AbstractType(
    string name, string namespaceName, Scope scope, MistType? declaringType, TypeDeclarationNode declarationNode)
    : MemberType(name, namespaceName, scope, declaringType, declarationNode),
    IAbstractType
{
    public List<TypeSlot> TypeSlotsList { get; } = [];

    IReadOnlyList<ITypeSlot> IAbstractType.TypeSlots => TypeSlotsList;
}