using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A dependent parameter read through a value, <c>ring.Capacity</c>: the value the receiver's type was
/// closed with, typed by that argument as <c>arr.Length</c> is, met through the closing of the slot's
/// declaring type that the receiver reaches with its own arguments applied along the lookup's path; a
/// member named on the read answers through the slot's domain. A bare <c>unknown</c> in the slot
/// carries no domain, so that read is typed by the slot's own contract instead. The one read with no
/// answer is the slot of an abstract the receiver's type leaves <c>unknown</c>, because an object may
/// implement that abstract at more than one value (runtime-dependent-values Requirement 11.2).
/// </summary>
internal static class InstanceSlotRead
{
    public static TypeParameter? Parameter(LookupResult found)
    {
        return found is TypeResult { Type: TypeParameter { IsDependent: true, IsPack: false } slot } ? slot : null;
    }

    public static IMistType? TypeOf(TypeParameter slot, ConstructedType? closing)
    {
        if(closing is null || slot.Ordinal >= closing.TypeArguments.Count)
        {
            return null;
        }

        var argument = closing.TypeArguments[slot.Ordinal];
        return argument is UnknownType ? Contract(slot) ?? argument : argument;
    }

    public static IMistType? Contract(TypeParameter slot)
    {
        return slot switch
        {
            { Domain: { } domain, RefinementNode: { } predicateNode, Refinement: { } predicate } => new RefinedType(domain, predicateNode) { Predicate = predicate },
            { Domain: { } domain } => domain,
            _ => null,
        };
    }

    public static ConstructedType? Closing(TypeResult found, ConstructedType? receiver, TypeInterner? interner)
    {
        if(found.Type is not TypeParameter { DeclaringType: IHasTypeParameters declaring and MistType definition })
        {
            return null;
        }

        return TypeSubstitution
            .ForPath(receiver, found.BaseConstructions, interner)
            .Apply(new ConstructedType(definition, declaring.TypeParametersList), interner) as ConstructedType;
    }

    public static ConstructedType? ThroughUnknown(TypeParameter slot, ConstructedType? closing)
    {
        return closing is { Definition: AbstractType }
            && slot.Ordinal < closing.TypeArguments.Count
            && closing.TypeArguments[slot.Ordinal] is UnknownType or UnknownValueType
                ? closing
                : null;
    }
}