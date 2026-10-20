using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The field-type restriction on an owned type (owned-fields Requirements 4, 5 and 11.2): each instance field is
/// an owned type, clonable as <see cref="OwnedProvenance.IsClonable"/> decides, or a value type, and a type
/// parameter is constrained to one of those. Only a reference type is judged, so a type the model cannot place
/// is admitted rather than refused, and the restriction reads no provenance (5.3).
/// </summary>
internal static class OwnedFieldTypes
{
    public static string? WhyRefused(Field field)
    {
        if(field.Modifiers.IsStatic)
        {
            return null;
        }

        var type = Unaliased(field.Type);

        if(type is TypeParameter)
        {
            return Admits(type) ? null : "a type parameter not constrained to an owned type or 'IClonable<T>'";
        }

        return IsReference(type) && !Admits(type) ? "which is neither owned, clonable nor a value type" : null;
    }

    private static bool IsReference(IMistType type)
    {
        return type is ClassType or AbstractType or IFunctionType or ConstructedType { Definition: ClassType or AbstractType };
    }

    private static bool Admits(IMistType type)
    {
        return Unaliased(type) switch
        {
            ConcreteType { IsOwned: true } or ConstructedType { Definition: ConcreteType { IsOwned: true } } => true,
            TypeParameter parameter when parameter.ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Owned
                || (constraint is TypeConstraint { ConstraintType: var bound } && Admits(bound))) => true,
            var unaliased => OwnedProvenance.IsClonable(unaliased),
        };
    }

    private static IMistType Unaliased(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Unaliased(aliased),
            RefinedType refined => Unaliased(refined.BaseType),
            _ => type,
        };
    }
}