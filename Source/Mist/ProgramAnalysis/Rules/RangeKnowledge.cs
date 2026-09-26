using System.Collections.Immutable;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal static class RangeKnowledge
{
    public static RangeBound? Index(ImmutableDictionary<object, Knowledge> state, Expression value, LiftedReturns? lifted = null)
    {
        return RangeBound.Of(value, offset => Captured(state, offset, lifted),
            held => RefinementFlow.Holder(held) is { } holder && state.TryGetValue(holder, out var known) ? known.IndexValue : null,
            length => CapturedLength(state, length, lifted));
    }

    public static StoredRange? Range(ImmutableDictionary<object, Knowledge> state, RangeExpression value, LiftedReturns? lifted = null)
    {
        return StoredRange.Of(value, offset => Captured(state, offset, lifted),
            held => RefinementFlow.Holder(held) is { } holder && state.TryGetValue(holder, out var known) ? known.IndexValue : null,
            length => CapturedLength(state, length, lifted));
    }

    public static bool IsLength(MemberAccessExpression access)
    {
        var receiver = Requirement.Unrefined(access.Receiver.Type);

        return access.MemberName == "Length" && Lengths.IsSized(receiver) && access.Member switch
        {
            TypeResult { Type: TypeParameter { IsDependent: true, Ordinal: 1 } parameter }
                => receiver is ConstructedType constructed && ReferenceEquals(parameter.DeclaringType, constructed.Definition),
            PropertyResult { Property: { Modifiers.IsIntrinsic: true, DeclaringType: { } host } }
                => receiver.FullyQualifiedName == "Standard.String" && ReferenceEquals(host, receiver),
            _ => false,
        };
    }

    private static Admitted Captured(ImmutableDictionary<object, Knowledge> state, Expression value, LiftedReturns? lifted)
    {
        var declared = (Requirement.ValuesOf(value.Type) ?? Admitted.Everything).And(lifted?.Produced(value) ?? Admitted.Everything);

        return value is LocalReferenceExpression local && state.TryGetValue(local.Local, out var known)
            ? known.Values.And(declared)
            : declared;
    }

    private static RangeBound? CapturedLength(ImmutableDictionary<object, Knowledge> state, Expression value, LiftedReturns? lifted)
    {
        if(value is not MemberAccessExpression access || !IsLength(access))
        {
            return null;
        }

        var length = RefinementFlow.Length(state, access.Receiver, lifted) ?? Admitted.AtLeast(0);
        var holder = RefinementFlow.Holder(access.Receiver);

        return Lengths.Exact(length) is not null ? new RangeBound(length, null, false)
            : holder is not null ? new RangeBound(length, new LengthSymbol(holder), false) : null;
    }

    private sealed record LengthSymbol(object Holder);
}