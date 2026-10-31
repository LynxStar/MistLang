using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

internal sealed class StandardSignatureAmbientProvider : IAmbientTypeProvider
{
    public static StandardSignatureAmbientProvider Instance { get; } = new();

    public void BuildAndRegister(ProgramModelClass programModel)
    {
        foreach(var name in new[] { "StringComparison", "StringSplitOptions" })
        {
            _ = programModel.RegisterType(new EnumType(name, "System", null, null, null)
            {
                Assembly = MistAssembly.Ambient,
                Modifiers = new MemberModifiers { Accessibility = Accessibility.Public },
                UnderlyingType = programModel.TypesByFQN["Standard.Int32"],
            });
        }

        _ = programModel.RegisterType(new AbstractType("IFormatProvider", "System", null, null, null)
        {
            Assembly = MistAssembly.Ambient,
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public },
        });
    }
}