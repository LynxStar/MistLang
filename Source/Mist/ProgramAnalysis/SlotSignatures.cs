using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a member answers a slot, which abstract-type-unification Requirements 14.5 and 28.4 want
/// exact: the same arity, with a value pack the conformer bound written out
/// (<see cref="ValuePack.Expand"/>); every parameter passed the same way and a <c>ref</c> return
/// answered by one (ref-parameters Requirement 7.5); and every type AGREEING once the slot is read
/// through the abstract's type arguments with its own method type parameters renamed to the filler's
/// (<see cref="Closing"/>), and the filler through the base it was inherited from. Types agree part by
/// part and a type parameter only with itself, so a generic <c>Identity</c> returning <c>int</c> does not
/// answer one returning its own <c>T</c>. What still agrees with anything is what no substitution here
/// could have closed: an error, and a parameter DECLARED BY AN ABSTRACT, which surviving the slot's
/// substitution is one the conformer gave no argument for -- someone else's complaint already -- or an
/// associated type slot it left unfilled, which <see cref="ConformanceAnalyzer"/> reports on its own; a
/// filled slot is closed to its filler by the substitution the conformance passes. A generic answer's
/// constraints are not part of agreeing: <see cref="Strengthened"/> judges them, by entailment from the
/// slot's, once the answer is found. A refinement, named or a parameter's `where`, is
/// looked through, at the top and in a construction's arguments: it is the same signature over a
/// narrower set of values, and whether the filler narrows MORE than the slot promised is the refinement
/// rule's to judge. Inside a tuple, union or function type it is not, those signatures being nominal.
/// Ownership is no part of passing: an `owned` return is <see cref="ReturnsOwned"/>'s to judge.
/// </summary>
internal static class SlotSignatures
{
    public static bool Agree(IMistType slot, IMistType filler)
    {
        return Agree(slot, filler, nominal: false);
    }

    public static bool Answers(Method slot, Method filler, TypeSubstitution substitution, TypeInterner interner)
    {
        return Answers(slot, filler, substitution, TypeSubstitution.Empty, interner);
    }

    public static bool Answers(
        Method slot, Method filler, TypeSubstitution slotSubstitution, TypeSubstitution fillerSubstitution, TypeInterner? interner)
    {
        if(slot.TypeParametersList.Count != filler.TypeParametersList.Count || !SameReturnPassing(slot.Modifiers, filler.Modifiers))
        {
            return false;
        }

        var closing = Closing(slot, filler, slotSubstitution);

        return Agree(closing.Apply(slot.ReturnType, interner), fillerSubstitution.Apply(filler.ReturnType, interner))
            && ParametersAgree(
                ValuePack.Expand(slot.ParametersList, closing, interner), TypeSubstitution.Empty, filler.ParametersList, fillerSubstitution, interner);
    }

    public static bool Answers(
        Indexer slot, Indexer filler, TypeSubstitution slotSubstitution, TypeSubstitution fillerSubstitution, TypeInterner? interner)
    {
        return SameReturnPassing(slot.Modifiers, filler.Modifiers)
            && Agree(slotSubstitution.Apply(slot.Type, interner), fillerSubstitution.Apply(filler.Type, interner))
            && ParametersAgree(slot.ParametersList, slotSubstitution, filler.ParametersList, fillerSubstitution, interner);
    }

    public static TypeSubstitution Closing(Method slot, Method filler, TypeSubstitution substitution)
    {
        return substitution.Extend(TypeSubstitution.ForParameters(slot.TypeParametersList, [.. filler.TypeParametersList]));
    }

    public static IEnumerable<(TypeParameter Parameter, TypeParameterConstraint Constraint)> Strengthened(
        Method slot, Method filler, TypeSubstitution slotSubstitution, TypeSubstitution fillerSubstitution, SyntaxNode at, TypeInterner interner)
    {
        var closing = Closing(slot, filler, slotSubstitution);

        for(var i = 0; i < slot.TypeParametersList.Count && i < filler.TypeParametersList.Count; i++)
        {
            var answering = filler.TypeParametersList[i];
            var promised = slot.TypeParametersList[i].ConstraintsList.Select(constraint => ConstraintAnalyzer.Closed(constraint, closing, interner)).ToList();

            if(promised.Contains(null))
            {
                continue;
            }

            var offered = new TypeParameter(answering.Name, answering.Ordinal, answering.Scope, answering.DeclaringType)
            {
                IsDependent = answering.IsDependent,
                Domain = answering.Domain,
            };
            offered.ConstraintsList.AddRange(promised!);

            foreach(var required in answering.ConstraintsList)
            {
                if(ConstraintEntailment.Decide(new TypeArgumentClosing(answering, offered, at, fillerSubstitution), required, interner) is not null)
                {
                    yield return (answering, required);
                }
            }
        }
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
            parameter => Display(parameter.Modifier.Direction, parameter.Type)));
        var owned = ReturnsOwned(slot) ? "owned " : "";
        var generic = slot.TypeParametersList.Count > 0 ? $"<{string.Join(", ", slot.TypeParametersList.Select(parameter => parameter.Name))}>" : "";

        return $"{owned}{Returned(slot.Modifiers)}{TypeDisplay.Mist(substitution.Apply(slot.ReturnType, interner))} {slot.Name}{generic}({parameters})";
    }

    public static string Display(Indexer slot, TypeSubstitution substitution, TypeInterner interner)
    {
        return $"{Returned(slot.Modifiers)}{TypeDisplay.Mist(substitution.Apply(slot.Type, interner))} {Name(slot, substitution, interner)}";
    }

    public static string Name(Indexer slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var parameters = string.Join(", ", slot.ParametersList.Select(
            parameter => Display(parameter.Modifier.Direction, substitution.Apply(parameter.Type, interner))));

        return $"this[{parameters}]";
    }

    private static bool Agree(IMistType slot, IMistType filler, bool nominal)
    {
        if(nominal && (IsNamed(slot) || IsNamed(filler)))
        {
            return slot.FullyQualifiedName == filler.FullyQualifiedName;
        }

        slot = Structural(slot);
        filler = Structural(filler);

        if(ReferenceEquals(slot, filler) || IsIndeterminate(slot) || IsIndeterminate(filler))
        {
            return true;
        }

        return (slot, filler) switch
        {
            (PackElementType left, PackElementType right) => ReferenceEquals(left.Pack, right.Pack) && left.Index == right.Index,
            (TypeParameter, _) or (_, TypeParameter) => false,
            (ConstructedType left, ConstructedType right) => Agree(left.Definition, right.Definition, nominal)
                && AgreeEach(left.TypeArguments, right.TypeArguments, nominal)
                && AgreeEnclosing(left.Enclosing, right.Enclosing, nominal),
            (TupleType left, TupleType right) => AgreeEach(left.ElementTypes, right.ElementTypes, nominal: true),
            (UnionType left, UnionType right) => left.Constituents.Count == right.Constituents.Count
                && left.Constituents.All(constituent => right.Constituents.Any(other => Agree(constituent, other, nominal: true))),
            (FunctionType left, FunctionType right) => left.IsPure == right.IsPure
                && Agree(left.ReturnType, right.ReturnType, nominal: true)
                && AgreeEach(left.ParameterTypes, right.ParameterTypes, nominal: true),
            (SpreadType left, SpreadType right) => Agree(left.Operand, right.Operand, nominal),
            (LiftedType { Value: TypePackValue left }, LiftedType { Value: TypePackValue right }) => AgreeEach(left.Elements, right.Elements, nominal),
            (ConstructedType or TupleType or UnionType or FunctionType or SpreadType, _) => false,
            (_, ConstructedType or TupleType or UnionType or FunctionType or SpreadType) => false,
            _ => slot.FullyQualifiedName == filler.FullyQualifiedName,
        };
    }

    private static bool ParametersAgree(
        IReadOnlyList<Parameter> slot, TypeSubstitution slotSubstitution, IReadOnlyList<Parameter> filler, TypeSubstitution fillerSubstitution,
        TypeInterner? interner)
    {
        if(slot.Count != filler.Count)
        {
            return false;
        }

        for(var i = 0; i < slot.Count; i++)
        {
            if(slot[i].Modifier.Direction != filler[i].Modifier.Direction
                || !Agree(slotSubstitution.Apply(slot[i].Type, interner), fillerSubstitution.Apply(filler[i].Type, interner)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool SameReturnPassing(MemberModifiers slot, MemberModifiers filler)
    {
        return slot.IsRef == filler.IsRef && slot.ReturnsReadonlyRef == filler.ReturnsReadonlyRef;
    }

    private static bool AgreeEach(IReadOnlyList<IMistType> slot, IReadOnlyList<IMistType> filler, bool nominal)
    {
        return slot.Count == filler.Count && slot.Zip(filler).All(pair => Agree(pair.First, pair.Second, nominal));
    }

    private static bool AgreeEnclosing(ConstructedType? slot, ConstructedType? filler, bool nominal)
    {
        return slot is null || filler is null ? slot is null && filler is null : Agree(slot, filler, nominal);
    }

    private static string Display(ByRefDirection direction, IMistType type)
    {
        var passing = direction switch
        {
            ByRefDirection.Ref => "ref ",
            ByRefDirection.In => "readonly ref ",
            ByRefDirection.Out => "out ",
            _ => "",
        };

        return $"{passing}{TypeDisplay.Mist(type)}";
    }

    private static string Returned(MemberModifiers modifiers)
    {
        return !modifiers.IsRef ? "" : modifiers.ReturnsReadonlyRef ? "readonly ref " : "ref ";
    }

    private static bool IsIndeterminate(IMistType type)
    {
        return type switch
        {
            ErrorType or NotProjectedType => true,
            PackElementType element => IsIndeterminate(element.Pack),
            TypeParameter parameter => parameter.DeclaringType is AbstractType,
            ConstructedType { Definition: TypeParameter definition } => IsIndeterminate(definition),
            _ => false,
        };
    }

    private static bool IsNamed(IMistType type)
    {
        return type is INamedTupleType or RefinedType or TypeAlias;
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