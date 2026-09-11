using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a member answers a slot: the same arity, and a return type and parameter types that AGREE
/// -- identical by name once the abstract's own type arguments are applied, structurally through a
/// construction, or indeterminate on either side. A type parameter the conformer supplies no argument
/// for, and an associated type slot, which the conformer's nested type IS by another name, both count
/// as agreeing, since reading either as a mismatch refuses <c>IntBox : IBox&lt;int&gt;</c> and every
/// conformer of an abstract with a slot-typed signature; the cost is that such a slot is checked on
/// arity alone.
/// </summary>
internal static class SlotSignatures
{
    public static bool Agree(IMistType slot, IMistType filler)
    {
        slot = Structural(slot);
        filler = Structural(filler);

        if(IsIndeterminate(slot) || IsIndeterminate(filler))
        {
            return true;
        }

        if(slot is ConstructedType left && filler is ConstructedType right)
        {
            return left.Definition.FullyQualifiedName == right.Definition.FullyQualifiedName
                && left.TypeArguments.Count == right.TypeArguments.Count
                && left.TypeArguments.Zip(right.TypeArguments).All(pair => Agree(pair.First, pair.Second));
        }

        return slot.FullyQualifiedName == filler.FullyQualifiedName;
    }

    public static bool Answers(Method slot, Method filler, TypeSubstitution substitution, TypeInterner interner)
    {
        if(slot.ParametersList.Count != filler.ParametersList.Count
            || slot.TypeParametersList.Count != filler.TypeParametersList.Count
            || !Agree(substitution.Apply(slot.ReturnType, interner), filler.ReturnType))
        {
            return false;
        }

        for(var i = 0; i < slot.ParametersList.Count; i++)
        {
            if(!Agree(substitution.Apply(slot.ParametersList[i].Type, interner), filler.ParametersList[i].Type))
            {
                return false;
            }
        }

        return true;
    }

    public static string Display(Method slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var parameters = string.Join(", ", slot.ParametersList.Select(
            parameter => TypeDisplay.Mist(substitution.Apply(parameter.Type, interner))));

        return $"{TypeDisplay.Mist(substitution.Apply(slot.ReturnType, interner))} {slot.Name}({parameters})";
    }

    private static bool IsIndeterminate(IMistType type)
    {
        return type is TypeParameter or ErrorType or UnknownType or ConstructedType { Definition: TypeParameter };
    }

    private static IMistType Structural(IMistType type)
    {
        return type is INamedTupleType named ? named.Underlying : type;
    }
}