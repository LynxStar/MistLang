using Mist.ProgramModel.Contracts;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

internal sealed class NullAmbientTypeProvider : IAmbientTypeProvider
{
    public static NullAmbientTypeProvider Instance { get; } = new();

    public void BuildAndRegister(ProgramModelClass programModel)
    {
    }
}