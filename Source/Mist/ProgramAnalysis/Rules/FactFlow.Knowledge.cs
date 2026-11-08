using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class FactFlow
{
    private static FactState LearnSide(
        FactState state, Expression condition, bool holds)
    {
        if(condition is BinaryExpression { OperatorKind: BinaryOperatorKind.NotEqual })
        {
            holds = !holds;
        }

        return condition switch
        {
            BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true }, Left: MemberAccessExpression { MemberName: "Length", Receiver: var sizedOne }, Right: var limit2 } sized
                            when RangeKnowledge.IsLength((MemberAccessExpression)sized.Left) && Holder(sizedOne) is { } oneHolder && Comparison(sized.OperatorKind) is { } sizedKind && Requirement.Constant(limit2) is { } sizedBound => Sized(state, oneHolder, Requirement.Interval(sizedKind, sizedBound), holds),
            BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true }, Left: var limit3, Right: MemberAccessExpression { MemberName: "Length", Receiver: var sizedTwo } } sizedFlip
                            when RangeKnowledge.IsLength((MemberAccessExpression)sizedFlip.Right) && Holder(sizedTwo) is { } twoHolder && Comparison(sizedFlip.OperatorKind) is { } flipKind && Requirement.Constant(limit3) is { } flipBound => Sized(state, twoHolder, Requirement.Interval(Mirrored(flipKind), flipBound), holds),
            IsPatternExpression { Scrutinee: LocalReferenceExpression realSubject, Pattern: var realPattern }
                            when RealRequirement.KindOf(realSubject.Type) is { } realKind && RealRequirement.Of(realPattern, realKind) is { } realMatched
                            => RealKnowledge.Narrow(state, realSubject.Local, holds ? realMatched : realMatched.Not()),
            IsPatternExpression { Scrutinee: LocalReferenceExpression listed, Pattern: ListPattern list } when holds => Sliced(state, listed, list),
            _ => state,
        };
    }

    private static FactState Sliced(
        FactState state, LocalReferenceExpression scrutinee, ListPattern list)
    {
        var fixedCount = list.Elements.Count(element => !element.IsSlice);
        var sliced = list.Elements.Any(element => element.IsSlice);
        var length = (Held(state, scrutinee) ?? Lengths.Typed(scrutinee.Type) ?? Admitted.Everything)
            .And(sliced ? Admitted.AtLeast(fixedCount) : Admitted.Exactly(fixedCount));

        state = Sized(state, scrutinee.Local, length, holds: true);

        if(list.Elements.FirstOrDefault(element => element.IsSlice)?.Subpattern?.Binding is { } middle)
        {
            state = state.SetItem(middle, Knowledge.Unknown with { Length = length.Shifted(-fixedCount) });
        }

        return state;
    }

    /// <summary>
    /// A length learned from a condition, and never negative -- which every caller here already assumes of
    /// the member it read. Without that, the negation of an EQUALITY carries nothing usable: `Length == 0`
    /// failing leaves the values below zero beside those above it, so `Length >= 1` does not follow, where
    /// the negation of an inequality needs no such help.
    /// </summary>
    private static FactState Sized(
        FactState state, object measured, Admitted admitted, bool holds)
    {
        var known = state.TryGetValue(measured, out var existing) ? existing : Knowledge.Unknown;
        var learned = (holds ? admitted : admitted.Not()).And(Admitted.AtLeast(0));

        return state.SetItem(measured, known with { Length = known.Length is { } already ? already.And(learned) : learned });
    }

    public static Admitted? Held(FactState state, Expression? receiver)
    {
        return Holder(receiver) is { } holder && state.TryGetValue(holder, out var known)
            ? known.Length
            : null
            ;
    }

    public static StoredRange? Range(FactState state, Expression index, LiftedReturns? lifted = null)
    {
        return index switch
        {
            RangeExpression range => RangeKnowledge.Range(state, range, lifted),
            LocalReferenceExpression local when state.TryGetValue(local.Local, out var known) => known.Slice,
            _ => null,
        };
    }

    public static Admitted? Length(FactState state, Expression? expression, LiftedReturns? lifted = null)
    {
        return Lengths.Of(expression, held => Held(state, held), range => Range(state, range, lifted));
    }

    public static bool Refused(FactState state, Expression expression, LiftedReturns? lifted = null)
    {
        return expression switch
        {
            ImplicitConversionExpression conversion => Refused(state, conversion.Operand, lifted),
            CloneExpression clone => Refused(state, clone.Source, lifted),
            LocalReferenceExpression local => state.TryGetValue(local.Local, out var known) && known.RefusedSlice,
            FieldReferenceExpression or MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult }
                => Holder(expression) is { } field && state.TryGetValue(field, out var held) && held.RefusedSlice,
            IndexExpression slice when Lengths.IsRangeSlice(slice) => Refused(state, slice.Receiver, lifted)
                || (Range(state, slice.Index, lifted) is { } range && Lengths.SafetyOf(range, Length(state, slice.Receiver, lifted)) == RangeSafety.Violated),
            _ => false,
        };
    }

    public static object? Holder(Expression? array)
    {
        return array switch
        {
            LocalReferenceExpression local => local.Local,
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult { Field: var field } } => field,
            _ => null,
        };
    }

    private static ComparisonKind? Comparison(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual => ComparisonKind.Equal,
            BinaryOperatorKind.LessThan => ComparisonKind.LessThan,
            BinaryOperatorKind.GreaterThan => ComparisonKind.GreaterThan,
            BinaryOperatorKind.LessThanOrEqual => ComparisonKind.LessThanOrEqual,
            BinaryOperatorKind.GreaterThanOrEqual => ComparisonKind.GreaterThanOrEqual,
            _ => null,
        };
    }

    private static ComparisonKind Mirrored(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.LessThan => ComparisonKind.GreaterThan,
            ComparisonKind.GreaterThan => ComparisonKind.LessThan,
            ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
            ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
            _ => kind,
        };
    }

    private static ComparisonKind Flipped(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
            ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
            ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
            ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThan,
            _ => kind,
        };
    }
}