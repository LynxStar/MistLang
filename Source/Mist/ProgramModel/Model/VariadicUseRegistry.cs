using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// The pack widths each generic METHOD is called at, recorded as calls are bound.
/// <para>
/// Recorded rather than searched for, because a call site is the only thing that knows: a method's
/// own declaration names no width, and an INFERRED call writes none either -- the width is what
/// overload resolution worked out. Sitting beside the constraint closures, which exist for the same
/// reason and are read by the same consumer.
/// </para>
/// <para>
/// A fact about the program, not about any target. What is DONE with it -- emitting one C# method
/// per width, since C# has no pack -- belongs to the backend and is no part of this.
/// </para>
/// </summary>
internal sealed class VariadicUseRegistry
{
    private readonly Dictionary<Method, SortedDictionary<string, IReadOnlyList<int>>> widths = [];

    public void Record(Method method, IReadOnlyList<IMistType> typeArguments)
    {
        var observed = new List<int>();

        for(var i = 0; i < method.TypeParametersList.Count && i < typeArguments.Count; i++)
        {
            if(!method.TypeParametersList[i].IsPack)
            {
                continue;
            }

            // One call fixes EVERY pack the method declares or none of them, which is why the
            // widths are recorded together rather than one per pack: a method with two packs used
            // at (2, 1) needs that declaration, not the four the cross product would name.
            if(typeArguments[i] is not LiftedType { Value: TypePackValue pack }
                || pack.Elements.Any(element => element is SpreadType))
            {
                return;
            }

            observed.Add(pack.Elements.Length);
        }

        if(observed.Count > 0)
        {
            _ = Record(method, observed);
        }
    }

    /// <summary>
    /// False when this method was already recorded at these widths.
    /// </summary>
    public bool Record(Method method, IReadOnlyList<int> observed)
    {
        if(!widths.TryGetValue(method, out var recorded))
        {
            recorded = new(StringComparer.Ordinal);
            widths[method] = recorded;
        }

        return recorded.TryAdd(string.Join("_", observed), observed);
    }

    public IEnumerable<(Method Method, IReadOnlyList<int> Widths)> All()
    {
        foreach(var (method, recorded) in widths)
        {
            foreach(var observed in recorded.Values)
            {
                yield return (method, observed);
            }
        }
    }

    /// <summary>
    /// The width combinations this method was called at, empty when it has no pack or was never
    /// called.
    /// </summary>
    public IReadOnlyCollection<IReadOnlyList<int>> WidthsOf(Method method)
    {
        return widths.TryGetValue(method, out var recorded) ? recorded.Values : [];
    }
}