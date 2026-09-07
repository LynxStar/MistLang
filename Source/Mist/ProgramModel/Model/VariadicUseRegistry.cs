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
    private readonly Dictionary<Method, SortedSet<int>> widths = [];

    public void Record(Method method, IReadOnlyList<IMistType> typeArguments)
    {
        for(var i = 0; i < method.TypeParametersList.Count && i < typeArguments.Count; i++)
        {
            if(method.TypeParametersList[i].IsPack
                && typeArguments[i] is LiftedType { Value: TypePackValue pack }
                && !pack.Elements.Any(element => element is SpreadType))
            {
                if(!widths.TryGetValue(method, out var observed))
                {
                    observed = [];
                    widths[method] = observed;
                }

                _ = observed.Add(pack.Elements.Length);
            }
        }
    }

    /// <summary>
    /// The widths this method was called at, empty when it has no pack or was never called.
    /// </summary>
    public IReadOnlyCollection<int> WidthsOf(Method method)
    {
        return widths.TryGetValue(method, out var observed) ? observed : [];
    }
}