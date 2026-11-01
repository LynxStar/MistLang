using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

internal sealed class StandardContractAmbient(IAmbientTypeProvider? ambient = null) : IAmbientTypeProvider
{
    public void BuildAndRegister(ProgramModelClass programModel)
    {
        if(programModel.TypesByFQN.TryGetValue("Standard.Int32", out var integer))
        {
            _ = programModel.RegisterType(new AbstractType("IFormatProvider", "System", null, null, null));
            _ = programModel.RegisterType(new EnumType("NumberStyles", "System.Globalization", null, null, null)
            { UnderlyingType = integer });
            var comparer = new AbstractType("IComparer<>", "System.Collections.Generic", null, null, null);
            comparer.TypeParametersList.Add(new TypeParameter("T", 0, new Scope(null), comparer));
            _ = programModel.RegisterType(comparer);
        }

        ambient?.BuildAndRegister(programModel);
    }
}