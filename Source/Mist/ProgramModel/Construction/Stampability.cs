using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Whether a type's default is a flat value that can be stamped into every slot of a bare array or a
/// stackalloc, so that no slot is ever observed holding null: an intrinsic struct, or a tuple or
/// struct of stampable fields. Distinct from <see cref="Defaultability"/>, which asks whether an
/// instance can be BUILT -- a class with a parameterless constructor has a default and is still a
/// reference, and a struct with a string field has one and still stamps a null. A refinement's zero
/// need not satisfy its predicate, an enum's need not be a variant, and a newtype is neither primitive
/// nor struct. Revisiting a generic declaration is finite only when its next instance occurs inside
/// the earlier instance's arguments; this admits nested boxes and stops expanding storage cycles.
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

    private static string? WhyNot(IMistType type, List<Visit> visiting)
    {
        return type switch
        {
            ErrorType => null,
            RefinedType => "it is a refinement, whose zero need not satisfy its predicate",
            TypeAlias alias => AliasWhyNot(alias, null, visiting),
            Newtype => "it is a newtype, neither a primitive nor a pure struct",
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

    private static string? StructWhyNot(StructType type, ConstructedType? receiver, List<Visit> visiting)
    {
        return type switch
        {
            StructType { IsRef: true } => "it is a ref struct",
            StructType { IsIntrinsic: true } => null,
            _ => FieldsWhyNot(type, receiver, visiting),
        };
    }

    private static string? AliasWhyNot(TypeAlias alias, ConstructedType? receiver, List<Visit> visiting)
    {
        if(alias.AliasedType is not { } aliased)
        {
            return "it is not a resolved type";
        }

        if(!Enter(alias, receiver ?? (IMistType)alias, visiting))
        {
            return "it contains itself";
        }

        try
        {
            return WhyNot(TypeSubstitution.ForReceiver(receiver).Apply(aliased, interner: null), visiting);
        }
        finally
        {
            visiting.RemoveAt(visiting.Count - 1);
        }
    }

    private static string? FieldsWhyNot(StructType type, ConstructedType? receiver, List<Visit> visiting)
    {
        if(!Enter(type, receiver ?? (IMistType)type, visiting))
        {
            return "it contains itself";
        }

        try
        {
            var substitution = TypeSubstitution.ForReceiver(receiver);
            if(type.BaseType is { } baseType
                && WhyNot(substitution.Apply(baseType, interner: null), visiting) is { } baseReason)
            {
                return baseReason;
            }

            foreach(var field in Defaultability.InstanceFields(type))
            {
                var fieldType = substitution.Apply(field.Type, interner: null);
                if(WhyNot(fieldType, visiting) is not null)
                {
                    return $"field '{field.Name}' of '{type.Name}' has type '{TypeDisplay.Mist(fieldType)}', which is not stampable";
                }
            }

            foreach(var property in type.PropertiesList.Where(property => !property.Modifiers.IsStatic && property.HasBackingStorage))
            {
                var propertyType = substitution.Apply(property.Type, interner: null);
                if(WhyNot(propertyType, visiting) is not null)
                {
                    return $"backing storage for property '{property.Name}' of '{type.Name}' has type '{TypeDisplay.Mist(propertyType)}', which is not stampable";
                }
            }

            return null;
        }
        finally
        {
            visiting.RemoveAt(visiting.Count - 1);
        }
    }

    private static bool Enter(IMistType definition, IMistType instance, List<Visit> visiting)
    {
        for(var i = visiting.Count - 1; i >= 0; i--)
        {
            if(!ReferenceEquals(visiting[i].Definition, definition))
            {
                continue;
            }

            if(visiting[i].Instance is not ConstructedType previous
                || !Children(previous).Any(child => Contains(child, instance, [])))
            {
                return false;
            }

            break;
        }

        visiting.Add(new Visit(definition, instance));
        return true;
    }

    private static bool Contains(IMistType container, IMistType instance, HashSet<IMistType> visited)
    {
        return Same(container, instance)
            || (visited.Add(container) && Children(container).Any(child => Contains(child, instance, visited)));
    }

    private static IEnumerable<IMistType> Children(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => constructed.Enclosing is { } enclosing
                ? constructed.TypeArguments.Append(enclosing)
                : constructed.TypeArguments,
            ITupleType tuple => tuple.ElementTypes,
            TypeAlias { AliasedType: { } aliased } => [aliased],
            LiftedType { Value: TypePackValue pack } => pack.Elements,
            _ => [],
        };
    }

    private static bool Same(IMistType left, IMistType right)
    {
        if(left is NamedTupleType leftTuple)
        {
            left = leftTuple.Underlying;
        }

        if(right is NamedTupleType rightTuple)
        {
            right = rightTuple.Underlying;
        }

        if(ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (ConstructedType first, ConstructedType second) => ReferenceEquals(first.Definition, second.Definition)
                && SameElements(first.TypeArguments, second.TypeArguments)
                && SameEnclosing(first.Enclosing, second.Enclosing),
            (ITupleType first, ITupleType second) => SameElements(first.ElementTypes, second.ElementTypes),
            (LiftedType { Value: TypePackValue first } firstLifted, LiftedType { Value: TypePackValue second } secondLifted) =>
                Same(firstLifted.Type, secondLifted.Type) && SameElements(first.Elements, second.Elements),
            (LiftedType first, LiftedType second) => Same(first.Type, second.Type) && Equals(first.Value, second.Value),
            _ => false,
        };
    }

    private static bool SameEnclosing(ConstructedType? left, ConstructedType? right)
    {
        return left is null || right is null ? ReferenceEquals(left, right) : Same(left, right);
    }

    private static bool SameElements(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        return left.Count == right.Count && left.Zip(right).All(pair => Same(pair.First, pair.Second));
    }

    private sealed record Visit(IMistType Definition, IMistType Instance);
}