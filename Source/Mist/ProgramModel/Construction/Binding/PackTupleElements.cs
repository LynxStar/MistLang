using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The positions a tuple type holding a pack spread has, read the way a positional pattern reads them:
/// a spread is as many positions as its pack has elements, never the one element the type lists it as.
/// Where the spread is the whole tuple and its pack's width is open -- <c>(TArgs...)</c>, the type of
/// <c>params TArgs args</c> and of a <c>TArgs...</c> field -- there are no positions to count, only
/// the pack, which each specialization fixes.
/// </summary>
internal static class PackTupleElements
{
    public static TypeParameter? OpenPack(IMistType type)
    {
        return type is ITupleType { ElementTypes: [SpreadType { Operand: TypeParameter { IsPack: true, PackLength: null } pack }] }
            ? pack
            : null;
    }

    public static IReadOnlyList<IMistType>? Positions(IReadOnlyList<IMistType>? held)
    {
        if(held is null || !held.Any(element => element is SpreadType { Operand: TypeParameter { IsPack: true, PackLength: not null } }))
        {
            return held;
        }

        return [.. held.SelectMany(element => element is SpreadType { Operand: TypeParameter { IsPack: true, PackLength: { } length } pack }
            ? Enumerable.Range(0, length).Select(index => (IMistType)pack.ElementAt(index))
            : [element])];
    }
}