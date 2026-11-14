using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// Defaultability supplies the base default without checking the predicate, so storage needs a separate
/// proof. Only intrinsic zeros are decidable here; enum designated variants and class defaults are not.
/// </summary>
internal static class RefinedZero
{
    public static bool IsAdmitted(IMistType type)
    {
        return IsAdmitted(type, LiteralPredicates.MatchLimit);
    }

    internal static bool IsAdmitted(IMistType type, TimeSpan matchLimit)
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
            return LiteralPredicates.Decide(type, Zero(unrefined.FullyQualifiedName), matchLimit) == true;
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