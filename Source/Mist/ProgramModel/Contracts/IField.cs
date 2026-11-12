using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Contracts;

public interface IField
{
    string Name { get; }

    IMistType Type { get; }

    OwnershipKind Ownership { get; }

    MemberModifiers Modifiers { get; }
}