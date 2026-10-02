using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a member answers a slot: the same arity, with a value pack the conformer bound written out
/// (<see cref="ValuePack.Expand"/>), and a return type and parameter types that AGREE
/// -- identical by name once the abstract's own type arguments are applied, structurally through a
/// construction, or indeterminate on either side. A type parameter the conformer supplies no argument
/// for, and an associated type slot, which the conformer's nested type IS by another name, both count
/// as agreeing, since reading either as a mismatch refuses <c>IntBox : IBox&lt;int&gt;</c> and every
/// conformer of an abstract with a slot-typed signature; the cost is that such a slot is checked on
/// arity alone. A refinement, named or a parameter's `where`, is looked through: it is the same
/// signature over a narrower set of values, and whether the filler narrows MORE than the slot
/// promised is the refinement rule's to judge, not a reason to say no member answers at all.
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
        return Answers(slot, filler, substitution, TypeSubstitution.Empty, interner);
    }

    public static bool Answers(
        Method slot, Method filler, TypeSubstitution slotSubstitution, TypeSubstitution fillerSubstitution, TypeInterner? interner)
    {
        var parameters = ValuePack.Expand(slot.ParametersList, slotSubstitution, interner);

        if(parameters.Count != filler.ParametersList.Count
            || slot.TypeParametersList.Count != filler.TypeParametersList.Count
            || !Agree(slotSubstitution.Apply(slot.ReturnType, interner), fillerSubstitution.Apply(filler.ReturnType, interner)))
        {
            return false;
        }

        for(var i = 0; i < parameters.Count; i++)
        {
            if(!Agree(parameters[i].Type, fillerSubstitution.Apply(filler.ParametersList[i].Type, interner)))
            {
                return false;
            }
        }

        return true;
    }

    public static IEnumerable<ConformanceAnalyzer.Filler> ExactFirst(
        IEnumerable<ConformanceAnalyzer.Filler> fillers, Method slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var parameters = ValuePack.Expand(slot.ParametersList, substitution, interner);

        return fillers.OrderBy(filler => filler.Method is { } method && method.ParametersList.Count == parameters.Count
            && parameters.Zip(method.ParametersList).All(pair =>
                !IsIndeterminate(Structural(filler.Substitution.Apply(pair.Second.Type, interner)))
                || IsIndeterminate(Structural(pair.First.Type)))
            ? 0
            : 1);
    }

    public static bool ReturnsOwned(Method method)
    {
        return method.Modifiers.IsOwned || method.ReturnOwnership == OwnershipKind.Owned;
    }

    public static string Display(Method slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var parameters = string.Join(", ", ValuePack.Expand(slot.ParametersList, substitution, interner).Select(
            parameter => TypeDisplay.Mist(parameter.Type)));
        var owned = ReturnsOwned(slot) ? "owned " : "";

        return $"{owned}{TypeDisplay.Mist(substitution.Apply(slot.ReturnType, interner))} {slot.Name}({parameters})";
    }

    public static string Display(Indexer slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var parameters = string.Join(", ", slot.ParametersList.Select(
            parameter => TypeDisplay.Mist(substitution.Apply(parameter.Type, interner))));

        return $"{TypeDisplay.Mist(substitution.Apply(slot.Type, interner))} this[{parameters}]";
    }

    private static bool IsIndeterminate(IMistType type)
    {
        return type is TypeParameter or ErrorType or UnknownType or ConstructedType { Definition: TypeParameter };
    }

    private static IMistType Structural(IMistType type)
    {
        return type switch
        {
            INamedTupleType named => named.Underlying,
            RefinedType refined => Structural(refined.BaseType),
            TypeAlias { AliasedType: RefinedType refined } => Structural(refined),
            _ => type,
        };
    }
}