using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// What each argument of a call is judged against once a <c>params</c> parameter has taken its share:
/// the fixed parameters pair inward from both ends, and an argument the flattened slot absorbed pairs
/// with the element it lands in -- the array's element for <c>params T[]</c>, the pack element at its
/// position for a value pack the call closed. An argument is null where it is a spread, whose count
/// the binder already proved, and a target is null where nothing pairs with one argument: a pack still
/// open, or one holding another count than the call passed, which <see cref="Unpaired"/> answers for
/// the slot as a whole.
/// </summary>
internal static class ArgumentTargets
{
    public static IReadOnlyList<IMistType?>? Of(
        IReadOnlyList<Parameter>? declared, IReadOnlyList<IMistType> closed, IReadOnlyList<IMistType?> arguments, TypeInterner interner)
    {
        if(Absorbed(declared, closed, arguments) is not { } slot)
        {
            return declared is null || ValuePack.FlattenedSlot(declared) is null
                ? closed.Count == arguments.Count ? closed : null
                : null;
        }

        var targets = new IMistType?[arguments.Count];

        for(var i = 0; i < arguments.Count; i++)
        {
            var parameter = ValuePack.SlotOf(declared!, arguments.Count, i);

            targets[i] = parameter == slot.Parameter ? Element(slot, arguments, i, interner) : closed[parameter];
        }

        return targets;
    }

    public static UnpairedSlot? Unpaired(
        IReadOnlyList<Parameter>? declared, IReadOnlyList<IMistType> closed, IReadOnlyList<IMistType?> arguments, TypeInterner interner)
    {
        if(Absorbed(declared, closed, arguments) is not { } slot
            || Elements(slot.Type) is not { } elements
            || (elements.Count == slot.Count && !elements.Any(element => element is SpreadType)))
        {
            return null;
        }

        var passed = arguments.Skip(slot.Parameter).Take(slot.Count).ToList();

        return passed.Contains(null)
            ? null
            : new UnpairedSlot(interner.GetOrCreateTupleType(passed!), slot.Type, slot.Parameter, slot.Count);
    }

    private static IMistType? Element(Slot slot, IReadOnlyList<IMistType?> arguments, int index, TypeInterner interner)
    {
        if(Indexability.ArrayElement(slot.Type) is { } element)
        {
            return slot.Count == 1
                && arguments[index] is { } alone
                && Indexability.ArrayElement(alone) is not null
                && Assignability.Judge(alone, element, interner) is not null
                    ? slot.Type
                    : element;
        }

        return Elements(slot.Type) is { } elements && elements.Count == slot.Count && !elements.Any(each => each is SpreadType)
            ? elements[index - slot.Parameter]
            : null;
    }

    private static Slot? Absorbed(IReadOnlyList<Parameter>? declared, IReadOnlyList<IMistType> closed, IReadOnlyList<IMistType?> arguments)
    {
        if(declared is null || declared.Count != closed.Count || ValuePack.FlattenedSlot(declared) is not { } flattened)
        {
            return null;
        }

        var count = arguments.Count - (declared.Count - 1);

        return count < 0 ? null : new Slot(flattened, closed[flattened], count);
    }

    private static IReadOnlyList<IMistType>? Elements(IMistType type)
    {
        return type switch
        {
            INamedTupleType named => named.Underlying.ElementTypes,
            ITupleType tuple => tuple.ElementTypes,
            _ => null,
        };
    }

    private readonly record struct Slot(int Parameter, IMistType Type, int Count);
}

internal sealed record UnpairedSlot(IMistType From, IMistType To, int First, int Count);