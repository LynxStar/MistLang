using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramModel.Construction.Binding;

internal static class TupleCorrelation
{
    public static bool MayMatch(Pattern pattern, IMistType type)
    {
        var admitted = MatchedValues.Admits(pattern, type);
        var domain = Requirement.ValuesOf(type);
        return admitted is null || !(domain is null ? admitted : admitted.And(domain)).Equals(Admitted.Nothing);
    }
}