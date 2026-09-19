using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model;

/// <summary>
/// The type arguments every call to a generic method was bound with, recorded as calls are bound for
/// the reason <see cref="VariadicUseRegistry"/> records widths: an inferred call writes no type
/// argument, so the call site is the only thing that knows. Arguments written inside a generic body
/// are recorded as they were bound, still open; what a target does with them is no part of this.
/// </summary>
internal sealed class GenericCallRegistry
{
    private readonly Dictionary<Method, List<IReadOnlyList<IMistType>>> calls = [];

    public void Record(Method method, IReadOnlyList<IMistType> typeArguments)
    {
        if(method.TypeParametersList.Count == 0)
        {
            return;
        }

        if(!calls.TryGetValue(method, out var recorded))
        {
            recorded = [];
            calls[method] = recorded;
        }

        recorded.Add(typeArguments);
    }

    public IEnumerable<(Method Method, IReadOnlyList<IMistType> TypeArguments)> All()
    {
        foreach(var (method, recorded) in calls)
        {
            foreach(var typeArguments in recorded)
            {
                yield return (method, typeArguments);
            }
        }
    }
}