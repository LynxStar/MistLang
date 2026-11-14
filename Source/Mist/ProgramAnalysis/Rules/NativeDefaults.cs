using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// CLR dispatch requires one most-specific declaration among the slot and its restatements.
/// A bodiless winner or incomparable tie leaves the slot to the conformer.
/// </summary>
internal static class NativeDefaults
{
    public static bool Answer(Method slot, AbstractType owner, IReadOnlyList<AbstractType> reached)
    {
        var candidates = new List<(AbstractType Declaring, bool HasBody)> { (owner, slot.HasNativeBody) };

        foreach(var restating in reached)
        {
            foreach(var (restated, isAbstract) in restating.NativeSlotOverridesList)
            {
                if(ReferenceEquals(restated, slot))
                {
                    candidates.Add((restating, !isAbstract));
                }
            }
        }

        var specific = candidates.FindAll(candidate => !candidates.Exists(
            other => !ReferenceEquals(other.Declaring, candidate.Declaring) && Derives(other.Declaring, candidate.Declaring)));

        return specific is [{ HasBody: true }];
    }

    private static bool Derives(AbstractType derived, AbstractType ancestor)
    {
        return AbstractInheritance.Reached(derived.BaseAbstractTypes, interner: null)
            .Any(reached => ReferenceEquals(reached.Definition, ancestor));
    }
}