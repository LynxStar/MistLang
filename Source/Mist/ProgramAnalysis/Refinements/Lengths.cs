using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
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

    public static Admitted? Of(Expression? expression, Func<Expression, Admitted?> tracked)
    {
        return expression switch
        {
            null => null,
            ImplicitConversionExpression conversion => Of(conversion.Operand, tracked),
            CloneExpression clone => Of(clone.Source, tracked),
            ArrayCreationExpression creation => Constant(creation.Size),
            StackallocExpression stack => Constant(stack.Size),
            ArrayLiteralExpression literal => Admitted.Exactly(literal.Elements.Count),
            LiteralExpression { Value: string text } => Admitted.Exactly(text.Length),
            LocalReferenceExpression or FieldReferenceExpression => tracked(expression) ?? Typed(expression.Type),
            NewExpression { Arguments: [var source] } => Of(source, tracked),
            InvocationExpression invocation => OfCall(invocation, tracked),
            IndexExpression { Index: RangeExpression range } slice => OfSlice(range, Of(slice.Receiver, tracked)),
            _ => Typed(expression.Type),
        };
    }

    private static Admitted? OfCall(InvocationExpression invocation, Func<Expression, Admitted?> tracked)
    {
        var receiver = invocation.Receiver is MemberAccessExpression access ? access.Receiver : null;
        var name = invocation.Method?.Name;

        return (name, invocation.Arguments) switch
        {
            ("AsSpan", []) => Of(receiver, tracked),
            ("ToArray", []) => Of(receiver, tracked),
            ("ToCharArray", []) => Of(receiver, tracked),
            ("Slice", [_, var count]) => Constant(count),
            ("Slice", [var start]) => Shorten(Of(receiver, tracked), Constant(start)),
            _ => null,
        };
    }

    private static Admitted? OfSlice(RangeExpression range, Admitted? source)
    {
        var from = range.Start is null ? Admitted.Exactly(0) : Position(range.Start, source);
        var to = range.End is null ? source : Position(range.End, source);

        return Exact(to) is { } high && Exact(from) is { } low && high >= low
            ? Admitted.Exactly(high - low)
            : null
            ;
    }

    private static Admitted? Position(Expression bound, Admitted? source)
    {
        if(bound is UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd, Operand: var offset })
        {
            return Exact(source) is { } length && Exact(Constant(offset)) is { } k
                ? Admitted.Exactly(length - k)
                : null
                ;
        }

        return Constant(bound);
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