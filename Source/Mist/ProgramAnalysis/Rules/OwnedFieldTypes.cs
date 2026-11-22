using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// This restriction uses declared types, not provenance (owned-fields Requirement 5.3), and admits
/// types the model cannot classify as references. <see cref="CloneDerivation"/> decides cloning.
/// An owned base judges its own storage, so the inherited-slot walk stops there.
/// </summary>
internal static class OwnedFieldTypes
{
    public static IEnumerable<Violation> Of(ConcreteType owned, CloneDerivation clones)
    {
        var visited = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance);
        var substitution = TypeSubstitution.Empty;

        for(var declaring = owned; visited.Add(declaring);)
        {
            foreach(var slot in InstanceStorage.Of(declaring))
            {
                var type = substitution.Apply(slot.Type, interner: null);

                if(WhyRefused(type, clones) is { } reason)
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

    public static string? WhyRefused(IMistType type, CloneDerivation clones)
    {
        type = Unaliased(type);

        if(type is UnionType union)
        {
            return union.Constituents.Where(constituent => !OptionalUnion.IsVoid(constituent)).Select(constituent => WhyRefused(constituent, clones)).FirstOrDefault(reason => reason is not null);
        }

        if(type is TypeParameter)
        {
            return Admits(type, clones) ? null : "a type parameter not constrained to an owned type or 'IClonable<T>'";
        }

        if(!IsReference(type) || Admits(type, clones))
        {
            return null;
        }

        return CloneDerivation.IsInMist((type as ConstructedType)?.Definition as MistType ?? type as MistType) && clones.Judge(type).Refusal is { } refusal
            ? $"which is neither owned, clonable nor a value type ({CloneDerivation.Describe(type, refusal)})"
            : "which is neither owned, clonable nor a value type";
    }

    private static bool IsReference(IMistType type)
    {
        return type is ClassType or AbstractType or IFunctionType or ConstructedType { Definition: ClassType or AbstractType };
    }

    private static bool Admits(IMistType type, CloneDerivation clones)
    {
        return Unaliased(type) switch
        {
            ConcreteType { IsOwned: true } or ConstructedType { Definition: ConcreteType { IsOwned: true } } => true,
            TypeParameter parameter when parameter.ConstraintsList.Exists(constraint => constraint.Kind is TypeParameterConstraintKind.Owned or TypeParameterConstraintKind.Struct
                || (constraint is TypeConstraint { ConstraintType: var bound } && Admits(bound, clones))) => true,
            var unaliased => clones.Judge(unaliased).Kind != CloneKind.Refused,
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