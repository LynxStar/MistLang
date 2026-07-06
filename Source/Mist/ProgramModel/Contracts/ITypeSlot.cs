using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A type slot declared on an abstract type. Conforming types must supply
/// a concrete type that satisfies the slot's constraints.
/// </summary>
public interface ITypeSlot
{
    /// <summary>The name of the type slot.</summary>
    string Name { get; }

    /// <summary>The modifiers declared on this type slot.</summary>
    MemberModifiers Modifiers { get; }
}