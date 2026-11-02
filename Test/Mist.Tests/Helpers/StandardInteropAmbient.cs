using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

internal sealed class StandardInteropAmbient(IAmbientTypeProvider? inner = null) : IAmbientTypeProvider
{
    public void BuildAndRegister(ProgramModelClass programModel)
    {
        inner?.BuildAndRegister(programModel);
        RegisterException(programModel);
    }

    public static void RegisterException(ProgramModelClass programModel)
    {
        if(programModel.FindQualifiedType("System.Exception") is not null
            || programModel.FindQualifiedType("Standard.String") is not { } text)
        {
            return;
        }

        var exception = new ClassType("Exception", "System", null, null, null)
        { Assembly = MistAssembly.Ambient, ClrMetadataName = "System.Exception" };
        // A CLR constructor supplies no Mist semantic default; an empty shell would admit a nonexistent default factory.
        exception.ConstructorsList.Add(new Constructor("Exception", exception, [], new MemberModifiers { Accessibility = Accessibility.Public, IsClrProjected = true })
        { DeclaringType = exception });
        exception.PropertiesList.Add(new Property("Message", text, new MemberModifiers { Accessibility = Accessibility.Public, IsClrProjected = true, ClrResultIsNonNull = true })
        {
            DeclaringType = exception,
            Getter = new PropertyAccessor { IsAuto = true },
        });
        _ = programModel.RegisterType(exception);
        _ = programModel.GetOrAddNamespace("System");
    }
}