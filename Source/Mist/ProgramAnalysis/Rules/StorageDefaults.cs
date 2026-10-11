using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A constructor starts from a slot's initializer or type default, independently of the owner's default
/// block. Both construction and default synthesis refuse a refinement's zero when its predicate does.
/// </summary>
internal static class StorageDefaults
{
    public static bool HasConstructorDefault(InstanceStorage slot)
    {
        return slot.Initializer is not null
            || (Defaultability.HasDefault(Requirement.Unrefined(slot.Type)) && RefinedZero.IsAdmitted(slot.Type));
    }

    public static bool ZeroRefused(ConcreteType owner, InstanceStorage slot)
    {
        return Defaultability.StartsFromZero(owner, slot) && !RefinedZero.IsAdmitted(slot.Type);
    }
}