using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal static class ResultTransfers
{
    public static Expression? Source(Expression expression)
    {
        return expression switch
        {
            ElementReadExpression { OwnershipSource: { } owner } => owner,
            ResultIdentityExpression identity => identity.Operand,
            ResultConstructionExpression construction => construction.Payload,
            ResultPropagationExpression propagation => propagation.Operand,
            MemberAccessExpression { MemberName: "Payload", Receiver: var receiver } when ResultTypes.Projection(receiver.Type) is not null => receiver,
            InvocationExpression { Receiver: MemberAccessExpression access, Method.Name: "Inspect" } when ResultTypes.Of(access.Receiver.Type) is not null => access.Receiver,
            InvocationExpression { Arguments: [var payload], Method: { Name: "Success" or "Failure", DeclaringType.FullyQualifiedName: "Standard.Result<,>" } } => payload,
            InvocationExpression { Receiver: MemberAccessExpression access, Method.Name: "AsSuccess" or "AsFailure" } call
                when ResultTypes.Projection(call.Type) is not null => access.Receiver,
            _ => null,
        };
    }

    public static SubstructuralDiscipline Discipline(IMistType type)
    {
        var payloads = Payloads(type);
        if(payloads.Count > 0)
        {
            var disciplines = payloads.Select(Discipline).ToList();
            return disciplines.Contains(SubstructuralDiscipline.Linear) ? SubstructuralDiscipline.Linear
                : disciplines.Contains(SubstructuralDiscipline.Affine) ? SubstructuralDiscipline.Affine
                : disciplines.Contains(SubstructuralDiscipline.Relevant) ? SubstructuralDiscipline.Relevant : SubstructuralDiscipline.None;
        }

        return (type is ConstructedType constructed ? constructed.Definition : type) is ConcreteType concrete
            ? concrete.Discipline
            : SubstructuralDiscipline.None;
    }

    public static bool IsOwned(Expression expression)
    {
        return expression is LocalReferenceExpression { Local.Ownership: OwnershipKind.Owned }
            || (Source(expression) is { } source && IsOwned(source))
            || expression is InvocationExpression { Method.Modifiers.IsOwned: true } or InvocationExpression { Method.ReturnOwnership: OwnershipKind.Owned };
    }

    public static IReadOnlyList<IMistType> Payloads(IMistType type)
    {
        if(ResultTypes.Of(type) is { } result)
        {
            return [result.Success, result.Error];
        }

        if(ResultTypes.Projection(type) is { } projection)
        {
            return [projection.Payload];
        }

        return Inspection(type) is { } inspection ? inspection.Constituents.Cast<ITupleType>().Select(tuple => tuple.ElementTypes[1]).ToList() : [];
    }

    public static UnionType? Inspection(IMistType type)
    {
        return type is UnionType { Constituents: [ITupleType { ElementTypes: [var first, _] }, ITupleType { ElementTypes: [var second, _] }] } union
            && Requirement.ValuesOf(first) is { } left && Requirement.ValuesOf(second) is { } right
            && ((left.Equals(Admitted.Exactly(0)) && right.Equals(Admitted.Exactly(1)))
                || (left.Equals(Admitted.Exactly(1)) && right.Equals(Admitted.Exactly(0))))
                ? union : null;
    }
}