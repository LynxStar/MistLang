using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Contracts;

public interface IProperty
{
    string Name { get; }

    IMistType Type { get; }

    MemberModifiers Modifiers { get; }
}