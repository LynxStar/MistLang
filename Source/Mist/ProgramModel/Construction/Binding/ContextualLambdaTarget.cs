using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal static class ContextualLambdaTarget
{
    // Only a lambda's destination may select a union arm; a union value is not callable.
    public static IFunctionType? Of(IMistType? target, TypeInterner interner)
    {
        return UniqueArmOf(target, interner) is { } arm ? DelegateShape.Of(arm) : null;
    }

    public static IMistType? GeneralUnionArmOf(IMistType? target, TypeInterner interner)
    {
        var expanded = AliasTransparency.Expand(target ?? ErrorType.Instance, interner);
        return expanded is UnionType && !OptionalUnion.IsOptional(expanded)
            ? UniqueArmOf(expanded, interner)
            : null;
    }

    public static bool IsGeneralUnion(IMistType? target, TypeInterner interner)
    {
        var expanded = AliasTransparency.Expand(target ?? ErrorType.Instance, interner);
        return expanded is UnionType && !OptionalUnion.IsOptional(expanded);
    }

    private static IMistType? UniqueArmOf(IMistType? target, TypeInterner interner)
    {
        IMistType? chosen = null;
        return Find(target ?? ErrorType.Instance, interner, ref chosen) ? chosen : null;
    }

    private static bool Find(IMistType target, TypeInterner interner, ref IMistType? chosen)
    {
        var expanded = AliasTransparency.Expand(target, interner);
        if(!ReferenceEquals(expanded, target))
        {
            return Find(expanded, interner, ref chosen);
        }

        if(expanded is UnionType union)
        {
            foreach(var arm in union.Constituents)
            {
                if(!Find(arm, interner, ref chosen))
                {
                    return false;
                }
            }

            return true;
        }

        if(DelegateShape.Of(expanded) is null)
        {
            return true;
        }

        if(chosen is not null)
        {
            return ConversionRelation.IsIdentity(chosen, expanded);
        }

        chosen = expanded;
        return true;
    }
}