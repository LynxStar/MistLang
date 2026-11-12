namespace Mist.ProgramModel.Contracts;

public interface IEnumMember
{
    string Name { get; }

    bool IsDefault { get; }
}