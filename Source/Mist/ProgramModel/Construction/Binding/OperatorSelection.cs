using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal sealed record OperatorChoice(IMistType? Type, ClosedOperator? Selected, IReadOnlyList<ClosedOperator> Tied)
{
    public static readonly OperatorChoice None = new(null, null, []);

    public static OperatorChoice Typed(IMistType? type)
    {
        return new OperatorChoice(type, null, []);
    }
}

/// <summary>
/// Which declared operator an operator expression calls, chosen as a call chooses its overload
/// (standard-library Requirement 6.3). The candidates are the operators each operand's type and its base chain
/// declare, closed through the construction each was found on and the base clauses walked to reach it, so a
/// generic owner's operator takes that owner's type arguments; one declaration found through both operands on
/// one construction is one candidate, not a tie with itself. Applicability and ranking are the method rules,
/// but an integer constant reaches a narrower integral parameter only of a declared operator (method-overloading
/// Requirement 3.7), not of an intrinsic one: that is the target's own arithmetic, which promotes the constant
/// with its type, so a byte plus 1 stays Int32's sum rather than Byte's. Several left unbeaten are a tie for
/// the operator rule to report, never a choice.
/// </summary>
internal static class OperatorSelection
{
    public static (ClosedOperator? Selected, IReadOnlyList<ClosedOperator> Tied) Select(
        OperatorKind kind, IReadOnlyList<IMistType> operands, IReadOnlyList<Int128?>? constants, TypeInterner? interner)
    {
        var applicable = operands
            .SelectMany(operand => DeclaredOn(operand, kind, operands.Count, interner))
            .DistinctBy(candidate => (candidate.Declaration, candidate.Owner.FullyQualifiedName))
            .Where(candidate => Applies(candidate, operands, constants))
            .ToList();

        var unbeaten = applicable.FindAll(candidate => !applicable.Exists(
            other => !ReferenceEquals(other, candidate) && Beats(other, candidate, operands)));

        return unbeaten.DistinctBy(Describe).Count() == 1 ? (unbeaten[0], []) : (null, unbeaten);
    }

    public static IEnumerable<ClosedOperator> DeclaredOn(IMistType operand, OperatorKind kind, int arity, TypeInterner? interner)
    {
        var visited = new HashSet<MemberType>(ReferenceEqualityComparer.Instance);
        var closing = TypeSubstitution.Empty;

        for(var owner = OperatorAdmissibility.Underlying(operand); owner is not null;)
        {
            if((owner is ConstructedType constructed ? constructed.Definition : owner) is not MemberType definition || !visited.Add(definition))
            {
                yield break;
            }

            closing = closing.Extend(TypeSubstitution.ForReceiver(owner as ConstructedType));

            foreach(var declared in definition.OperatorsList)
            {
                if(declared.Kind == kind && declared.ParametersList.Count == arity)
                {
                    yield return new ClosedOperator(
                        declared,
                        owner,
                        closing,
                        [.. declared.ParametersList.Select(parameter => closing.Apply(parameter.Type, interner))],
                        closing.Apply(declared.ReturnType, interner));
                }
            }

            owner = definition is ConcreteType { BaseType: { } baseType } ? closing.Apply(baseType, interner) : null;
        }
    }

    public static string Describe(ClosedOperator candidate)
    {
        var parameters = string.Join(", ", candidate.ParameterTypes.Select(TypeDisplay.Mist));

        return $"{TypeDisplay.Mist(candidate.Owner)}.operator {candidate.Declaration.Symbol}({parameters})";
    }

    private static bool Applies(ClosedOperator candidate, IReadOnlyList<IMistType> operands, IReadOnlyList<Int128?>? constants)
    {
        for(var i = 0; i < operands.Count; i++)
        {
            var parameter = candidate.ParameterTypes[i];
            var constant = constants is not null && i < constants.Count ? constants[i] : null;

            if(!OperatorAdmissibility.Accepts(parameter, operands[i])
                && !(constant is { } value && !candidate.Declaration.Modifiers.IsIntrinsic && ConstantConversion.Fits(value, operands[i], parameter)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Beats(ClosedOperator left, ClosedOperator right, IReadOnlyList<IMistType> operands)
    {
        var strictlyBetterSomewhere = false;
        var identical = true;

        for(var i = 0; i < operands.Count; i++)
        {
            var comparison = OverloadResolution.CompareConversions(operands[i], left.ParameterTypes[i], right.ParameterTypes[i]);

            if(comparison > 0)
            {
                return false;
            }

            strictlyBetterSomewhere |= comparison < 0;
            identical &= ConversionRelation.IsIdentity(left.ParameterTypes[i], right.ParameterTypes[i]);
        }

        return strictlyBetterSomewhere
            || (identical && OverloadResolution.MoreSpecificSignature(left.Declaration, right.Declaration, operands.Count));
    }
}