using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// An abstract (interface-like) type definition in the program model.
/// </summary>
internal sealed class AbstractType(
    string name, string namespaceName, Scope? parentScope, MistType? declaringType, TypeDeclarationNode? declarationNode)
    : MemberType(name, namespaceName, parentScope, declaringType, declarationNode),
    IAbstractType
{
    public List<TypeSlot> TypeSlotsList { get; } = [];

    /// <summary>
    /// A projected interface's explicit restatements of slots it inherits: a body that supplies one, or a
    /// reabstraction that withdraws the inherited default. The CLR keeps both private, so neither is a
    /// member here, yet each settles whether a conformer reaching this interface must answer the slot.
    /// </summary>
    public List<(Members.Method Slot, bool IsAbstract)> NativeSlotOverridesList { get; } = [];

    IReadOnlyList<ITypeSlot> IAbstractType.TypeSlots => TypeSlotsList;
}