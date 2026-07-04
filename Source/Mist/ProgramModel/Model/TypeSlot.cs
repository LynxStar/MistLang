using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A type slot declared on an abstract type. Conforming types must supply
/// a concrete type that satisfies the slot's constraints.
/// </summary>
internal sealed class TypeSlot(
    string name,
    IReadOnlyList<IMistType> typeConstraints,
    MemberModifiers modifiers) : ITypeSlot
{
    public string Name { get; } = name;

    public IReadOnlyList<IMistType> TypeConstraints { get; } = typeConstraints;

    public MemberModifiers Modifiers { get; } = modifiers;
}