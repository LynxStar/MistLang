namespace Mist.ProgramModel.Contracts.Types;

public interface IAbstractType : IMemberType
{
    IReadOnlyList<ITypeSlot> TypeSlots { get; }
}