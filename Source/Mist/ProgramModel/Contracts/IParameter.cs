using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Contracts;

public interface IParameter
{
    string Name { get; }

    IMistType Type { get; }

    ParameterModifier Modifier { get; }
}