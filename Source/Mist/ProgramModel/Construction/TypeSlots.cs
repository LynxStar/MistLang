using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// An associated type slot and what fills it: the nested type on the conformer, or on a base of it,
/// with the slot's name and arity, or for a slot of no type parameters the type a nested alias of that
/// name stands for (associated-types glossary, Concrete_Type_Member). Held here rather than in the
/// backend's lift because both the lowering and the conformance rule ask the same question, and two
/// answers would drift.
/// </summary>
internal static class TypeSlots
{
    public static IEnumerable<IMistType> Reached(MemberType type)
    {
        foreach(var abstractBase in type.BaseAbstractTypes)
        {
            yield return abstractBase;
        }

        foreach(var conformance in (type as ConcreteType)?.ConformancesList ?? [])
        {
            yield return conformance.AbstractType;
        }
    }

    public static IMistType? Filler(TypeSlot slot, MemberType conformer)
    {
        var name = MistType.StripArity(slot.Name);

        foreach(var nested in conformer.NestedTypes)
        {
            if(nested is MemberType member
                && MistType.StripArity(member.Name) == name
                && member.TypeParametersList.Count == slot.TypeParametersList.Count)
            {
                return member;
            }

            if(nested is TypeAlias { TypeParametersList.Count: 0, AliasedType: { } aliased } alias
                && slot.TypeParametersList.Count == 0
                && alias.Name == name)
            {
                return aliased;
            }
        }

        return conformer is ConcreteType { BaseType: MemberType inherited }
            ? Filler(slot, inherited)
            : null
            ;
    }

    public static TypeSubstitution Closing(MemberType conformer, IEnumerable<AbstractType> reached)
    {
        var slots = new List<TypeParameter>();
        var fillers = new List<IMistType?>();

        foreach(var slot in reached.SelectMany(abstractType => abstractType.TypeSlotsList).Where(slot => slot.TypeParametersList.Count == 0))
        {
            slots.Add(slot);
            fillers.Add(Filler(slot, conformer));
        }

        return TypeSubstitution.ForParameters(slots, fillers);
    }
}