using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The field-type restriction on an owned type (owned-fields Requirements 4, 5 and 11.2): each instance storage
/// slot, inherited ones included (3.1), is an owned type, clonable as <see cref="OwnedProvenance.IsClonable"/>
/// decides, or a value type, and a type parameter is constrained to one of those. A union is judged by its
/// constituents. Only a reference type is judged, so a type the model cannot place is admitted rather than
/// refused, and the restriction reads no provenance (5.3). An owned base judges its own slots, so the walk up
/// the base chain stops there.
/// </summary>
internal static class OwnedFieldTypes
{
    public static IEnumerable<Violation> Of(ConcreteType owned)
    {
        var visited = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance);
        var substitution = TypeSubstitution.Empty;

        for(var declaring = owned; visited.Add(declaring);)
        {
            foreach(var slot in InstanceStorage.Of(declaring))
            {
                var type = substitution.Apply(slot.Type, interner: null);

                if(WhyRefused(type) is { } reason)
                {
                    yield return new Violation(slot, ReferenceEquals(declaring, owned) ? null : declaring, type, reason);
                }
            }

            var baseType = declaring.BaseType is { } written ? substitution.Apply(written, interner: null) : null;

            if((baseType is ConstructedType constructed ? constructed.Definition : baseType) is not ConcreteType { IsOwned: false, IsIntrinsic: false } next)
            {
                yield break;
            }

            substitution = TypeSubstitution.ForReceiver(baseType as ConstructedType);
            declaring = next;
        }
    }

    public static string? WhyRefused(IMistType type)
    {
        type = Unaliased(type);

        if(type is UnionType union)
        {
            return union.Constituents.Where(constituent => !OptionalUnion.IsVoid(constituent)).Select(WhyRefused).FirstOrDefault(reason => reason is not null);
        }

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

    public sealed record Violation(InstanceStorage Slot, ConcreteType? InheritedFrom, IMistType Type, string Reason);
}