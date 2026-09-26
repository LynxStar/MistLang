using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Whether a type's default is a flat value that can be stamped into every slot of a bare array or a
/// stackalloc, so that no slot is ever observed holding null: an intrinsic struct, or a tuple or
/// struct of stampable fields. Distinct from <see cref="Defaultability"/>, which asks whether an
/// instance can be BUILT -- a class with a parameterless constructor has a default and is still a
/// reference, and a struct with a string field has one and still stamps a null. A refinement's zero
/// need not satisfy its predicate, an enum's need not be a variant, a newtype is neither primitive nor
/// struct, and a type parameter is never stampable whatever its constraints say, since no constraint
/// speaks of stamping.
/// </summary>
internal static class Stampability
{
    public static bool IsStampable(IMistType type)
    {
        return WhyNotStampable(type) is null;
    }

    public static string? WhyNotStampable(IMistType type)
    {
        return WhyNot(type, []);
    }

    private static string? WhyNot(IMistType type, HashSet<MemberType> visiting)
    {
        return type switch
        {
            RefinedType => "it is a refinement, whose zero need not satisfy its predicate",
            TypeAlias { AliasedType: { } aliased } => WhyNot(aliased, visiting),
            Newtype => "it is a newtype, neither a primitive nor a pure struct",
            EnumType => "it is an enum, whose zero need not be a variant",
            TupleType tuple => tuple.ElementTypes.Select(element => WhyNot(element, visiting)).FirstOrDefault(reason => reason is not null),
            ConstructedType constructed => WhyNot(constructed.Definition, visiting),
            TypeParameter => "it is a type parameter, which no constraint makes stampable",
            StructType { IsRef: true } => "it is a ref struct",
            StructType { IsIntrinsic: true } => null,
            StructType structType => FieldsWhyNot(structType, visiting),
            ClassType or IFunctionType => "it is a reference type",
            _ => "it is not a struct",
        };
    }

    private static string? FieldsWhyNot(StructType type, HashSet<MemberType> visiting)
    {
        if(!visiting.Add(type))
        {
            return "it contains itself";
        }

        try
        {
            if(type.BaseType is { } baseType && WhyNot(baseType, visiting) is { } baseReason)
            {
                return baseReason;
            }

            foreach(var field in Defaultability.InstanceFields(type))
            {
                if(WhyNot(field.Type, visiting) is not null)
                {
                    return $"field '{field.Name}' of '{type.Name}' has type '{TypeDisplay.Mist(field.Type)}', which is not stampable";
                }
            }

            return null;
        }
        finally
        {
            _ = visiting.Remove(type);
        }
    }
}