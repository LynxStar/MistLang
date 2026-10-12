using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Whether a native default answers a slot a conformer leaves unanswered, by the rule the CLR dispatches
/// with: of the slot's own declaration and every restatement among the interfaces the conformer reaches,
/// the most specific one decides, and it must be the only one. A body there answers; a declaration without
/// one, or a tie between two restatements neither of which derives from the other, leaves the slot to the
/// conformer, as C# would.
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