namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// An abstract (interface-like) type definition.
/// </summary>
public interface IAbstractType : IMemberType
{
    /// <summary>
    /// Type slots declared on this abstract type.
    /// </summary>
    IReadOnlyList<ITypeSlot> TypeSlots { get; }
}