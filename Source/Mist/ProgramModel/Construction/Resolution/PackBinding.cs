using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Assigns a flat use-site type-argument list to declared slots when one of them is a pack.
/// <para>
/// THE PACK TAKES THE MIDDLE, and the ends are what determine where the middle begins: the slots
/// before it bind the leading arguments one for one, the slots after it bind the trailing ones, and
/// whatever remains between -- possibly nothing -- becomes the pack. Leading and trailing are both
/// counted from their own end rather than from the pack, which is what lets the pack sit anywhere
/// in the list without the rule changing.
/// </para>
/// </summary>
internal static class PackBinding
{
    public static bool AcceptsSpread(IReadOnlyList<TypeParameter> parameters, int argumentCount, int index)
    {
        return AbsorbingSlot(parameters) is { } slot
            && index >= slot && index < argumentCount - (parameters.Count - slot - 1);
    }

    /// <summary>
    /// The index of the slot that absorbs the middle, or null when this declaration has none and
    /// the ordinary one-for-one rule applies.
    /// </summary>
    public static int? AbsorbingSlot(IReadOnlyList<TypeParameter> parameters)
    {
        for(var i = 0; i < parameters.Count; i++)
        {
            if(parameters[i].PackKind == TypePackKind.Unbracketed)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// The declared slots filled from a flat argument list, with the absorbing slot holding the
    /// arguments the fixed ones did not claim. Null when there are too few to fill the fixed slots,
    /// which the caller reports as MIST_VAR_0005.
    /// </summary>
    public static IReadOnlyList<IMistType>? Bind(
        IReadOnlyList<TypeParameter> parameters,
        IReadOnlyList<IMistType> arguments,
        int absorbing,
        TypeInterner interner,
        IMistType packDomain)
    {
        var trailing = parameters.Count - absorbing - 1;

        // The pack may absorb nothing, so the fixed slots are the whole of the requirement.
        if(arguments.Count < parameters.Count - 1)
        {
            return null;
        }

        var bound = new List<IMistType>(parameters.Count);

        for(var i = 0; i < absorbing; i++)
        {
            bound.Add(arguments[i]);
        }

        var absorbed = arguments.Skip(absorbing).Take(arguments.Count - absorbing - trailing)
            .SelectMany(argument => argument is SpreadType { Operand: LiftedType { Value: TypePackValue spliced } } ? spliced.Elements : [argument])
            .ToList();

        // Already a pack when the use site wrote brackets, in which case it is adopted rather than
        // wrapped -- nesting it would make Foo<[int, string]> a different type from Foo<int, string>
        // when the whole point of the bracketed form is that they are the same one.
        var value = absorbed is [LiftedType { Value: TypePackValue supplied }]
            ? supplied
            : new TypePackValue(absorbed);

        bound.Add(interner.LiftPack(packDomain, value));

        for(var i = arguments.Count - trailing; i < arguments.Count; i++)
        {
            bound.Add(arguments[i]);
        }

        return bound;
    }
}