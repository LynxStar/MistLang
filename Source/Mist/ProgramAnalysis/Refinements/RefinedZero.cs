using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// Whether a refinement admits its base's zero, which is where storage of it starts when nothing is
/// written: Defaultability gives a refinement its base's default without reading the predicate. Only an
/// intrinsic base has a zero decided here, an enum's designated variant or a class's default being no
/// constant the prover can test, so any other base is not admitted.
/// </summary>
internal static class RefinedZero
{
    public static bool IsAdmitted(IMistType type)
    {
        if(Requirement.Refined(type) is null)
        {
            return true;
        }

        if(Requirement.Unrefined(type) is not ConcreteType { IsIntrinsic: true } unrefined)
        {
            return false;
        }

        if(LiteralPredicates.Applies(type))
        {
            return LiteralPredicates.Decide(type, Zero(unrefined.FullyQualifiedName)) == true;
        }

        return Requirement.OfType(type) is { Bounds.Length: 0 } required && required.Values.Contains(0);
    }

    private static LiteralValue Zero(string name)
    {
        if(name == WellKnownTypes.String.FullyQualifiedName)
        {
            return new LiteralValue("", name);
        }

        if(name == WellKnownTypes.Single.FullyQualifiedName)
        {
            return new LiteralValue(0f, name);
        }

        return name == WellKnownTypes.Decimal.FullyQualifiedName ? new LiteralValue(0m, name) : new LiteralValue(0d, name);
    }
}