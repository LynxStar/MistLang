using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Contracts;

public interface IIndexer
{
    IMistType Type { get; }

    IReadOnlyList<IParameter> Parameters { get; }

    MemberModifiers Modifiers { get; }
}