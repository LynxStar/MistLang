using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// <see cref="Defaultability"/>'s answer for a slot of instance storage, less a refinement whose predicate
/// refuses the base's zero the slot would start from. ProgramModel cannot read a predicate, so the rule for
/// defaults and the rule for constructors both finish the answer here.
/// </summary>
internal static class StorageDefaults
{
    public static bool HasDefault(ConcreteType owner, InstanceStorage slot)
    {
        return Defaultability.StorageHasDefault(owner, slot) && !ZeroRefused(owner, slot);
    }

    public static bool ZeroRefused(ConcreteType owner, InstanceStorage slot)
    {
        return Defaultability.StartsFromZero(owner, slot) && !RefinedZero.IsAdmitted(slot.Type);
    }
}