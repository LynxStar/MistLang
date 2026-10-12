using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

internal static class RefStructs
{
    public static bool Is(IMistType type)
    {
        var unrefined = Requirement.Unrefined(type);
        return (unrefined is ConstructedType constructed ? constructed.Definition : unrefined) is StructType { IsRef: true };
    }
}