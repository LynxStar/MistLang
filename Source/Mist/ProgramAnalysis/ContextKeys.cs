using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// The identity an ambient value is filed and found under, from structured-context Requirement 5, and
/// the one place it is decided. An alias and a refinement key as what they stand for, a refinement
/// narrowing which values may be offered and not where they are kept. A construction, a union, a tuple
/// and a function type key as themselves, part by part, so a holder of int is not a holder of string,
/// <c>int | bool</c> is not <c>int</c>, and a pure function type is not the plain one. Nothing is found
/// through a base type. A type parameter is only ever itself: what it keys as is each caller's to say,
/// so two that share a name are still two.
/// </summary>
internal static class ContextKeys
{
    public static bool Same(IMistType left, IMistType right)
    {
        left = Keyed(left);
        right = Keyed(right);

        if(ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (TypeParameter, _) or (_, TypeParameter) => false,
            (ConstructedType first, ConstructedType second) => Same(first.Definition, second.Definition)
                && SameEach(first.TypeArguments, second.TypeArguments)
                && SameEach(Enclosing(first), Enclosing(second)),
            (UnionType first, UnionType second) => first.Constituents.Count == second.Constituents.Count
                && first.Constituents.All(constituent => second.Constituents.Any(other => Same(constituent, other))),
            (FunctionType first, FunctionType second) => first.IsPure == second.IsPure
                && Same(first.ReturnType, second.ReturnType)
                && SameEach(first.ParameterTypes, second.ParameterTypes),
            (TupleType first, TupleType second) => SameEach(first.ElementTypes, second.ElementTypes),
            _ => string.Equals(left.FullyQualifiedName, right.FullyQualifiedName, StringComparison.Ordinal),
        };
    }

    public static bool IsOpen(IMistType type)
    {
        var keyed = Keyed(type);

        return keyed is TypeParameter or PackElementType or SpreadType || Declarations.Parts(keyed).Any(IsOpen);
    }

    public static bool IsNameable(IMistType type)
    {
        var keyed = Keyed(type);

        return keyed is not AnonymousType && Declarations.Parts(keyed).All(IsNameable);
    }

    private static IMistType Keyed(IMistType type)
    {
        while(true)
        {
            switch(type)
            {
                case TypeAlias { AliasedType: { } aliased }:
                    type = aliased;
                    break;
                case RefinedType refined:
                    type = refined.BaseType;
                    break;
                case NamedTupleType named:
                    type = named.Underlying;
                    break;
                default:
                    return type;
            }
        }
    }

    private static IMistType[] Enclosing(ConstructedType constructed)
    {
        return constructed.Enclosing is { } enclosing ? [enclosing] : [];
    }

    private static bool SameEach(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        return left.Count == right.Count && left.Zip(right).All(pair => Same(pair.First, pair.Second));
    }
}