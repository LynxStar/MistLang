using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A type slot declared on an abstract type. Conforming types must supply
/// a concrete type that satisfies the slot's constraints.
/// </summary>
internal sealed class TypeSlot(
    string name,
    MemberModifiers modifiers) : ITypeSlot
{
    public string Name { get; } = name;
    public MemberModifiers Modifiers { get; } = modifiers;
}