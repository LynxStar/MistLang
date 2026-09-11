using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel;

/// <summary>
/// The optional-union predicates shared by the binder and the backend. An optional is
/// EXACTLY a two-constituent union with void; it alone bypasses the carrier struct, so a
/// wider union that happens to contain void (<c>int | string | void</c>) is not an
/// optional and keeps its carrier.
/// </summary>
internal static class OptionalUnion
{
    public static bool IsVoid(IMistType type)
    {
        return string.Equals(
            type.FullyQualifiedName, WellKnownTypes.Void.FullyQualifiedName, StringComparison.Ordinal);
    }

    public static bool ContainsVoid(IMistType type)
    {
        return type is UnionType union && HasVoidConstituent(union);
    }

    public static bool IsOptional(IMistType type)
    {
        return type is UnionType { Constituents.Count: 2 } union && HasVoidConstituent(union);
    }

    public static bool AdmitsConditionalAccess(IMistType type)
    {
        var definition = type is ConstructedType constructed ? constructed.Definition : type;

        return ContainsVoid(type)
            || type is UnknownType or UnknownValueType or TypeParameter or ErrorType or NotProjectedType
            || definition is DeclaredType { Assembly.HasMistSource: false };
    }

    public static IMistType? InnerType(IMistType type)
    {
        if(!IsOptional(type))
        {
            return null;
        }

        var union = (UnionType)type;

        return union.Constituents.First(constituent => !IsVoid(constituent));
    }

    private static bool HasVoidConstituent(UnionType union)
    {
        foreach(var constituent in union.Constituents)
        {
            if(IsVoid(constituent))
            {
                return true;
            }
        }

        return false;
    }
}