using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A dependent parameter read through a value, <c>ring.Capacity</c>: the value the receiver's type was
/// closed with, met through the closing of the slot's declaring type that the receiver reaches with its
/// own arguments applied along the lookup's path. The read is typed by the domain written after the slot's
/// colon whatever the closing holds and whatever <c>where</c> the slot declares, so <c>a.Length</c> is a
/// <c>ZeroOrPositiveInt</c> and <c>ring.Capacity</c> an <c>int</c> where a type belongs; what the read holds,
/// the <c>where</c> included, is known through the receiver's type, which Stage 5 reads
/// (runtime-dependent-values Requirement 7). The one read with no answer is the slot of an abstract the
/// receiver's type leaves <c>unknown</c>, because an object may implement that abstract at more than one
/// value (Requirement 11.2).
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
        return argument is ErrorType ? argument : slot.Domain ?? argument;
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