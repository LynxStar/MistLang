using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Whether a type's default is a flat value that can be stamped into every slot of a bare array or a
/// stackalloc, so that no slot is ever observed holding null: an intrinsic struct, or a tuple or
/// struct of stampable fields. Distinct from <see cref="Defaultability"/>, which asks whether an
/// instance can be BUILT -- a class with no constructor of its own has a default and is still a
/// reference, and a struct with a string field has one and still stamps a null. A refinement's zero
/// need not satisfy its predicate, an enum's need not be a variant, and a newtype is neither primitive
/// nor struct. See <see cref="StorageRecursion"/> for the finite recursive storage proof.
/// </summary>
internal static class Stampability
{
    public static bool IsStampable(IMistType type)
    {
        return WhyNotStampable(type) is null;
    }

    public static string? WhyNotStampable(IMistType type)
    {
        return WhyNot(type, new StorageRecursion());
    }

    private static string? WhyNot(IMistType type, StorageRecursion visiting)
    {
        return type switch
        {
            ErrorType => null,
            RefinedType => "it is a refinement, whose zero need not satisfy its predicate",
            TypeAlias alias => AliasWhyNot(alias, null, visiting),
            Newtype => "it is a newtype, neither a primitive nor a struct",
            EnumType => "it is an enum, whose zero need not be a variant",
            ITupleType tuple => tuple.ElementTypes.Select(element => WhyNot(element, visiting)).FirstOrDefault(reason => reason is not null),
            ConstructedType { Definition: StructType definition } constructed => StructWhyNot(definition, constructed, visiting),
            ConstructedType { Definition: TypeAlias alias } constructed => AliasWhyNot(alias, constructed, visiting),
            ConstructedType constructed => WhyNot(constructed.Definition, visiting),
            TypeParameter parameter => !parameter.IsPack && !parameter.IsDependent
                && parameter.ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Stampable)
                ? null
                : "it is a type parameter without a stampable constraint",
            StructType structType => StructWhyNot(structType, null, visiting),
            ClassType or IFunctionType => "it is a reference type",
            _ => "it is not a struct",
        };
    }

    private static string? StructWhyNot(StructType type, ConstructedType? receiver, StorageRecursion visiting)
    {
        return type switch
        {
            StructType { IsRef: true } => "it is a ref struct",
            StructType { IsIntrinsic: true } => null,
            _ => FieldsWhyNot(type, receiver, visiting),
        };
    }

    private static string? AliasWhyNot(TypeAlias alias, ConstructedType? receiver, StorageRecursion visiting)
    {
        if(alias.AliasedType is not { } aliased)
        {
            return "it is not a resolved type";
        }

        if(!visiting.Enter(alias, receiver ?? (IMistType)alias))
        {
            return "it contains itself";
        }

        try
        {
            return WhyNot(TypeSubstitution.ForReceiver(receiver).Apply(aliased, interner: null), visiting);
        }
        finally
        {
            visiting.Exit();
        }
    }

    private static string? FieldsWhyNot(StructType type, ConstructedType? receiver, StorageRecursion visiting)
    {
        if(!visiting.Enter(type, receiver ?? (IMistType)type))
        {
            return "it contains itself";
        }

        try
        {
            var layout = ValueStorage.Describe(receiver ?? (IMistType)type);
            if(!layout.IsComplete)
            {
                return "its complete instance storage is unknown";
            }

            if(layout.BaseType is { } baseType && WhyNot(baseType, visiting) is { } baseReason)
            {
                return baseReason;
            }

            foreach(var field in layout.Fields)
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
            visiting.Exit();
        }
    }

}