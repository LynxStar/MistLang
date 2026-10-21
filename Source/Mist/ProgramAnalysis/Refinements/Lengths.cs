using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// What is known of a sequence's length. A length reaches the prover two ways: the TYPE carries it,
/// as <c>int[3]</c> does, or the flow does, because the expression a local was given says how long it
/// is and nothing since has reassigned it. The second is why this exists -- <c>int[] a = new int[3]</c>
/// erases the 3 the creation states, and a span or a slice carries no length in its type at all.
/// </summary>
internal static class Lengths
{
    public static bool IsIndexable(IMistType definition)
    {
        return definition.FullyQualifiedName is "Standard.Array<,>" or "Standard.Span<,>" or "Standard.ReadOnlySpan<,>";
    }

    public static bool IsSized(IMistType type)
    {
        var receiver = Requirement.Unrefined(type);

        return receiver.FullyQualifiedName == "Standard.String" || (receiver is ConstructedType { Definition: var definition } && IsIndexable(definition));
    }

    public static bool IsRangeSlice(IndexExpression index)
    {
        return Requirement.Unrefined(index.Index.Type).FullyQualifiedName == "Standard.Range" && IsSized(index.Receiver.Type);
    }

    public static Admitted? Typed(IMistType type)
    {
        var unaliased = type is TypeAlias { AliasedType: { } aliased } ? aliased : type;

        if(unaliased is not ConstructedType { Definition: var definition, TypeArguments: [_, LiftedType { Value: var length }] } || !IsIndexable(definition))
        {
            return null;
        }

        return length switch
        {
            long known => Admitted.Exactly(known),
            int known => Admitted.Exactly(known),
            _ => null,
        };
    }

    public static Admitted? Of(Expression? expression, Func<Expression, Admitted?> tracked, Func<Expression, StoredRange?>? ranges = null)
    {
        return expression switch
        {
            null => null,
            ImplicitConversionExpression conversion => Of(conversion.Operand, tracked, ranges),
            CloneExpression clone => Of(clone.Source, tracked, ranges),
            ArrayCreationExpression creation => Constant(creation.Size),
            StackallocExpression stack => Constant(stack.Size),
            ArrayLiteralExpression literal => Admitted.Exactly(literal.Elements.Count),
            LiteralExpression { Value: string body } => LiteralValue.Decoded(body) is { } text ? Admitted.Exactly(text.Length) : null,
            LocalReferenceExpression or FieldReferenceExpression or MemberAccessExpression { Member: FieldResult } => tracked(expression) ?? Typed(expression.Type),
            NewExpression { Arguments: [var source] } creation when SequenceLengths.CopiesArray(creation) => Of(source, tracked, ranges),
            InvocationExpression invocation => OfCall(invocation, tracked, ranges),
            IndexExpression slice when IsRangeSlice(slice) && (ranges?.Invoke(slice.Index) ?? (slice.Index is RangeExpression direct ? StoredRange.Of(direct) : null)) is { } range => OfSlice(range, Of(slice.Receiver, tracked, ranges)),
            _ => Typed(expression.Type),
        };
    }

    private static Admitted? OfCall(InvocationExpression invocation, Func<Expression, Admitted?> tracked, Func<Expression, StoredRange?>? ranges)
    {
        var (operation, source, arguments) = SequenceLengths.Of(invocation);
        return (operation, arguments) switch
        {
            (SequenceLengthOperation.Preserved, []) => Of(source, tracked, ranges) ?? Typed(invocation.Type),
            (SequenceLengthOperation.Slice, [_, var count]) => Constant(count) ?? Typed(invocation.Type),
            (SequenceLengthOperation.Slice, [var start]) => Shorten(Of(source, tracked, ranges), Constant(start)) ?? Typed(invocation.Type),
            _ => Typed(invocation.Type),
        };
    }

    public static Admitted? OfSlice(RangeExpression range, Admitted? source)
    {
        return StoredRange.Of(range) is { } stored ? OfSlice(stored, source) : null;
    }

    public static Admitted? OfSlice(StoredRange range, Admitted? source)
    {
        return SafetyOf(range, source) == RangeSafety.Violated ? null : Difference(range, source)?.And(Admitted.AtLeast(0));
    }

    public static RangeSafety SafetyOf(StoredRange range, Admitted? source)
    {
        var length = source ?? Admitted.AtLeast(0);
        var size = Difference(range, length)!;

        if(Outside(range.Start.Values, length) || Outside(range.End.Values, length) || size.High is < 0)
        {
            return RangeSafety.Violated;
        }

        return Inside(range.Start.Values, length) && Inside(range.End.Values, length) && size.Low is >= 0
            ? RangeSafety.Proven
            : RangeSafety.Unknown;
    }

    private static bool Outside(Admitted offset, Admitted length)
    {
        return offset.High is < 0 || (offset.Low is { } low && length.High is { } high && low > high);
    }

    private static bool Inside(Admitted offset, Admitted length)
    {
        return offset.Low is >= 0 && offset.High is { } high && length.Low is { } low && high <= low;
    }

    private static Admitted? Difference(StoredRange range, Admitted? source)
    {
        if(range.Start.FromEnd == range.End.FromEnd)
        {
            return range.Start.FromEnd ? range.Start.Values.Minus(range.End.Values) : range.End.Values.Minus(range.Start.Values);
        }

        if(source is null)
        {
            return null;
        }

        return range.End.FromEnd
            ? source.Minus(range.End.Values).Minus(range.Start.Values)
            : range.End.Values.Minus(source.Minus(range.Start.Values));
    }

    private static Admitted? Shorten(Admitted? source, Admitted? by)
    {
        return Exact(source) is { } length && Exact(by) is { } start && start <= length
            ? Admitted.Exactly(length - start)
            : null
            ;
    }

    private static Admitted? Constant(Expression expression)
    {
        return Requirement.Constant(expression) is { } value ? Admitted.Exactly(value) : null;
    }

    public static long? Exact(Admitted? admitted)
    {
        return admitted?.Intervals is [var only] && only.Low == only.High ? only.Low : null;
    }

    public static long? Shortest(Admitted? admitted)
    {
        return admitted?.Intervals is [var first, ..] && first.Low > long.MinValue ? first.Low : null;
    }
}

internal enum RangeSafety
{
    Unknown,
    Proven,
    Violated,
}