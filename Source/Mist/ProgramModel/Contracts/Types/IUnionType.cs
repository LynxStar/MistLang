namespace Mist.ProgramModel.Contracts.Types;

public interface IUnionType : IMistType
{
    IReadOnlyList<IMistType> Constituents { get; }
}