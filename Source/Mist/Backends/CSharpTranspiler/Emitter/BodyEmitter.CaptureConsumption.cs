using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Which captures a node consumes as a hidden argument and which it leaves for <see cref="EvaluateAhead"/> or a
/// discarded read, the half of <em>Identity by value: the first delivery</em>'s capture rule that reads the closing a
/// node writes rather than where its captures are evaluated.
/// </summary>
internal static partial class BodyEmitter
{
    private static IEnumerable<SlotCapture> ClosingCaptures(IMistType type)
    {
        return Unaliased(TypeNameEmitter.Closed(type)) is ConstructedType constructed
            ? SlotRoutes.ValueRouted(constructed.Definition)
                .Select(slot => SlotRoutes.Argument(constructed, slot))
                .OfType<CapturedValue>()
                .Select(captured => captured.Capture)
            : [];
    }

    private static IEnumerable<SlotCapture> NestedCaptures(IMistType type)
    {
        return Unaliased(TypeNameEmitter.Closed(type)) is ConstructedType constructed
            ? constructed.TypeArguments.SelectMany(argument => (StructStamp.HeldByValue(Unaliased(TypeNameEmitter.Closed(argument))) is null ? [] : ClosingCaptures(argument))
                .Concat(NestedCaptures(argument)))
            : [];
    }

    private static IEnumerable<SlotCapture> StampCaptures(IMistType element, Expression size, IReadOnlyList<SlotCapture> owned)
    {
        var ahead = size is LiteralExpression || IsPlainRead(size)
            ? []
            : owned.Where(capture => !ReferenceEquals(capture.Initializer, size) && !IsPlainRead(capture.Initializer) && ConsumedByClosing(element, capture));

        return ahead.Concat(NestedCaptures(element));
    }

    private static IEnumerable<SlotCapture> Unconsumed(Expression expression)
    {
        return expression switch
        {
            NewExpression creation => creation.Captures.Where(capture => !ConsumedByClosing(creation.Type, capture) && !ConsumedByWitness(creation.Type, capture)),
            DefaultExpression defaulted => defaulted.Captures.Where(capture => !ConsumedByClosing(defaulted.Type, capture) && !ConsumedByWitness(defaulted.Type, capture)),
            ArrayCreationExpression array => array.Captures.Where(capture => !ReferenceEquals(capture.Initializer, array.Size)
                && (!ConsumedByClosing(array.ElementType, capture) || KeptLocal(capture) is not null)),
            StackallocExpression stack => stack.Captures.Where(capture => !ReferenceEquals(capture.Initializer, stack.Size)
                && (!ConsumedByClosing(stack.ElementType, capture) || KeptLocal(capture) is not null)),
            InvocationExpression invocation => invocation.Captures.Where(capture => !ConsumedByCall(invocation, capture)
                && (KeptLocal(capture) is not null || !invocation.Arguments.Any(argument => ReferenceEquals(argument, capture.Initializer)))),
            MemberAccessExpression access => access.Captures.Where(capture => !ConsumedByAccess(access, capture)),
            _ => [],
        };
    }

    private static bool ConsumedByClosing(IMistType type, SlotCapture capture)
    {
        return Unaliased(TypeNameEmitter.Closed(type)) is ConstructedType constructed
            && SlotRoutes.ValueRouted(constructed.Definition).Any(slot =>
                SlotRoutes.Argument(constructed, slot) is CapturedValue captured
                && ReferenceEquals(captured.Capture, capture));
    }

    private static bool ConsumedByCall(InvocationExpression invocation, SlotCapture capture)
    {
        if(CallTypeArguments(invocation) is not var (arguments, parameters, method))
        {
            return false;
        }

        if(RoutedCallSlots(arguments, parameters).Any(routed => routed.Argument is CapturedValue captured && ReferenceEquals(captured.Capture, capture))
            || WitnessedCallSlots(arguments, parameters).Any(witnessed => WitnessCaptures(witnessed.Argument).Any(captured => ReferenceEquals(captured.Capture, capture)))
            || (KeptLocal(capture) is null && FactoredCallCaptures(arguments, parameters).Any(captured => ReferenceEquals(captured.Capture, capture))))
        {
            return true;
        }

        return method is { Modifiers.IsStatic: true, DeclaringType: MemberType }
            && invocation.Receiver is MemberAccessExpression { Receiver: TypeReferenceExpression reference }
            && (ConsumedByClosing(reference.ReferencedType, capture) || ConsumedByWitness(reference.ReferencedType, capture));
    }

    private static bool ConsumedByAccess(MemberAccessExpression access, SlotCapture capture)
    {
        if(ConsumedByHolder(access, capture))
        {
            return true;
        }

        return ProjectedValueAccess.Declaring(access) is { } declaring
            && access.Member is PropertyResult { Property.ProjectedParameter: { } parameter }
            && SlotRoutes.IsValueRouted(parameter)
            && Unaliased(TypeNameEmitter.Closed(declaring)) is ConstructedType closing
            && parameter.Ordinal < closing.TypeArguments.Count
            && closing.TypeArguments[parameter.Ordinal] is CapturedValue captured
            && ReferenceEquals(captured.Capture, capture);
    }
}