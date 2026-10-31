using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

internal sealed class StandardReflectionAmbient(IAmbientTypeProvider? inner) : IAmbientTypeProvider
{
    public void BuildAndRegister(ProgramModelClass programModel)
    {
        inner?.BuildAndRegister(programModel);
        RegisterDependencies(programModel);
    }

    public static void RegisterDependencies(ProgramModelClass programModel)
    {
        _ = programModel.GetOrAddNamespace("System.Reflection");
        foreach(var name in new[] { "Assembly", "MethodInfo" })
        {
            _ = programModel.RegisterType(new ClassType(name, "System.Reflection", null, null, null)
            {
                Assembly = MistAssembly.Ambient,
            });
        }
    }
}