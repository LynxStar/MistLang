using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal static class ContextDecision
{
    public static IEnumerable<(ContextBinding Earlier, ContextBinding Later)> Repeats(IReadOnlyList<ContextBinding> bindings)
    {
        for(var later = 1; later < bindings.Count; later++)
        {
            for(var earlier = 0; earlier < later; earlier++)
            {
                if(bindings[later].ContextType is not ErrorType && ContextKeys.Same(bindings[earlier].ContextType, bindings[later].ContextType))
                {
                    yield return (bindings[earlier], bindings[later]);
                    break;
                }
            }
        }
    }

    public static string? Contract(IReadOnlyList<IMistType> slot, IReadOnlyList<IMistType> answering)
    {
        if(slot.Concat(answering).Any(key => key is ErrorType))
        {
            return null;
        }

        var differences = slot.Where(required => !answering.Any(declared => ContextKeys.Same(required, declared)))
            .Select(required => $"`with {TypeDisplay.Mist(required)}` is missing")
            .Concat(answering.Where(declared => !slot.Any(required => ContextKeys.Same(required, declared)))
                .Select(declared => $"`with {TypeDisplay.Mist(declared)}` is not declared by the slot"))
            .ToList();

        return differences.Count == 0 ? null : string.Join("; ", differences);
    }
}