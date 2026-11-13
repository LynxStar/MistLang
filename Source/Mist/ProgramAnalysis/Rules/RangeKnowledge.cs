using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Rules;

internal static class RangeKnowledge
{
    public static RangeBound? Index(FactState state, Expression value, LiftedReturns? lifted = null)
    {
        return RangeBound.Of(value, offset => Captured(state, offset, lifted),
            held => FactFlow.Holder(held) is { } holder && state.TryGetValue(holder, out var known) ? known.IndexValue : null,
            length => CapturedLength(state, length, lifted));
    }

    public static StoredRange? Range(FactState state, RangeExpression value, LiftedReturns? lifted = null)
    {
        return StoredRange.Of(value, offset => Captured(state, offset, lifted),
            held => FactFlow.Holder(held) is { } holder && state.TryGetValue(holder, out var known) ? known.IndexValue : null,
            length => CapturedLength(state, length, lifted));
    }

    private static Admitted Captured(FactState state, Expression value, LiftedReturns? lifted)
    {
        var declared = (Requirement.ValuesOf(value.Type) ?? Admitted.Everything).And(lifted?.Produced(value) ?? Admitted.Everything);

        return value is LocalReferenceExpression local && state.TryGetValue(local.Local, out var known)
            ? known.Values.And(declared)
            : declared;
    }

    private static RangeBound? CapturedLength(FactState state, Expression value, LiftedReturns? lifted)
    {
        if(value is not MemberAccessExpression access || !SlotReads.IsLength(access))
        {
            return null;
        }

        var length = FactFlow.Length(state, access.Receiver, lifted) ?? Admitted.AtLeast(0);
        var holder = FactFlow.Holder(access.Receiver);

        return Lengths.Exact(length) is not null ? new RangeBound(length, null, false)
            : holder is not null ? new RangeBound(length, new LengthSymbol(holder), false) : null;
    }

    private sealed record LengthSymbol(object Holder);
}