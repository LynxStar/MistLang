using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The two optional typing moves. Lifting FLATTENS: a result that already contains void
/// stays as it is, so one level of absence is all an expression can carry -- and un-lifting
/// removes exactly that level. Both lean on the interner's normalization, which is what
/// collapses a single surviving constituent back to the bare type.
/// </summary>
internal static class OptionalLifting
{
    public static IMistType Lift(IMistType type, IMistType voidType, TypeInterner interner)
    {
        if(type is ErrorType || OptionalUnion.IsVoid(type) || OptionalUnion.ContainsVoid(type))
        {
            return type;
        }

        return interner.GetOrCreateUnionType([type, voidType]);
    }

    public static IMistType NonVoidPortion(IMistType type, TypeInterner interner)
    {
        if(type is not UnionType union || !OptionalUnion.ContainsVoid(union))
        {
            return type;
        }

        var rest = union.Constituents
            .Where(constituent => !OptionalUnion.IsVoid(constituent))
            .ToList()
            ;

        if(rest.Count == 0)
        {
            return type;
        }

        return interner.GetOrCreateUnionType(rest);
    }
}