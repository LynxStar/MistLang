namespace Mist.ProgramModel.Contracts.Types;

public interface IEnumType : IMistType, IHasAttributes
{
    IMistType? UnderlyingType { get; }

    IReadOnlyList<IEnumMember> Members { get; }
}