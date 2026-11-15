using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The value a value-routed slot carries at run time, as C#: a literal is itself, a dependent parameter is
/// its own read, and a capture is its initializer written where the node that owns it consumes it. A
/// method's inferred parameter is the one capture consumed away from its owner, so it is read back
/// through the argument that carries it, and that argument is bound once when reading it twice would run
/// it twice; a capture no argument carries is read from the local its owner kept it in. An extension
/// block's own parameter is read through the receiver that closed it, as its
/// length or its hidden field. The hidden argument comes first in the C# call, so the receiver and every
/// argument written before the carrier are bound with it, in source order, or they would run after it; a
/// value-type receiver is bound by what locates it, never copied. A
/// method group has no argument yet, so its lambda reads each slot from the delegate's parameter that
/// carries it, or from the receiver bound when the group was formed. A value-routed type's deferred
/// initializers run with the type as the enclosing one, so a static call they make finds its owner's
/// closing; an ordinary type's run as they always have, with none.
/// </summary>
internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static IMistType? patternSubject;

    internal static IDisposable Subject(IMistType? type)
    {
        return new PatternSubject(type);
    }

    private sealed class PatternSubject : IDisposable
    {
        private readonly IMistType? previous;

        public PatternSubject(IMistType? type)
        {
            previous = patternSubject;
            patternSubject = type;
        }

        public void Dispose()
        {
            patternSubject = previous;
        }
    }

    internal static bool Compared(IMistType tested)
    {
        return (RuntimeSlots(tested).Count > 0 || WitnessAsked(tested)) && !SlotSensitivity.AdmitsOnly(patternSubject, tested);
    }

    internal static IDisposable? Within(MemberType type)
    {
        return SlotRoutes.Carried(type).Count > 0 ? new Enclosing(type) : null;
    }

    private sealed class Enclosing : IDisposable
    {
        private readonly IMistType? previous;

        public Enclosing(MemberType type)
        {
            previous = enclosingType;
            enclosingType = type;
        }

        public void Dispose()
        {
            enclosingType = previous;
        }
    }

    internal static string? SlotValue(IMistType argument, DiagnosticBag? diagnostics)
    {
        if(StoredValue(argument) is { } stored)
        {
            return stored;
        }

        return TypeNameEmitter.Closed(argument) switch
        {
            LiftedType lifted => DependentCarrierNames.Literal(lifted.Value),
            TypeParameter parameter => DependentCarrierNames.ValueRead(parameter),
            CapturedValue captured => Evaluation(captured.Capture, AssignmentPrecedence, diagnostics ?? new DiagnosticBag()),
            _ => null,
        };
    }

    private static string? ProjectedValue(IMistType declaring, LookupResult? member, DiagnosticBag diagnostics)
    {
        return ProjectedValue(declaring, (member as PropertyResult)?.Property.ProjectedParameter, diagnostics);
    }

    private static string? ProjectedValue(IMistType declaring, TypeParameter? parameter, DiagnosticBag diagnostics)
    {
        return parameter is not null
            && SlotRoutes.IsValueRouted(parameter)
            && Unaliased(TypeNameEmitter.Closed(declaring)) is ConstructedType closing
            && SlotRoutes.Argument(closing, parameter) is { } argument
                ? SlotValue(argument, diagnostics)
                    ?? EmissionDiagnostics.Fallback("slot value", $"{TypeDisplay.Mist(closing)}.{parameter.Name}", "default")
                : null;
    }

    internal static List<string> SlotValues(IMistType? closing, DiagnosticBag? diagnostics, string? receiver = null)
    {
        var values = new List<string>();

        if(SlotRoutes.Closing(closing) is not { } constructed)
        {
            values.AddRange(ClosingWitnesses.Stored(closing).Select(SlotReach.Read));
            return values;
        }

        foreach(var slot in SlotRoutes.ValueRouted(constructed.Definition))
        {
            var argument = SlotRoutes.Argument(constructed, slot);
            values.Add(argument switch
            {
                null => EmissionDiagnostics.Fallback("slot value", slot.Name, "default"),
                _ when receiver is not null && TypeNameEmitter.Closed(argument) is TypeParameter carried && SlotRoutes.IsValueRouted(carried)
                    => $"{receiver}.{SlotRoutes.Hidden(carried)}",
                _ => SlotValue(argument, diagnostics) ?? EmissionDiagnostics.Fallback("slot value", $"{TypeDisplay.Mist(constructed)}.{slot.Name}", "default"),
            });
        }

        foreach(var parameter in ClosingWitnesses.Stored(constructed.Definition))
        {
            var argument = WitnessArgument(constructed, parameter);
            values.Add(receiver is not null && TypeNameEmitter.Closed(argument) is TypeParameter carried && ClosingWitnesses.IsWitnessed(carried)
                ? $"{receiver}.{ClosingWitnesses.Name(carried)}"
                : Witness(argument, diagnostics));
        }

        return values;
    }

    private static IMistType WitnessArgument(ConstructedType closing, TypeParameter parameter)
    {
        for(var step = closing; step is not null; step = step.Enclosing)
        {
            if(ReferenceEquals(parameter.DeclaringType, step.Definition) && parameter.Ordinal < step.TypeArguments.Count)
            {
                return step.TypeArguments[parameter.Ordinal];
            }
        }

        return parameter;
    }

    private static TypeParameter? RoutedTypeValue(IMistType type)
    {
        return SlotRoutes.ValueRouted(type) is [var routed, ..] ? routed : null;
    }

    internal static List<(TypeParameter Slot, IMistType Argument)> RuntimeSlots(IMistType tested)
    {
        var slots = new List<(TypeParameter, IMistType)>();

        if(SlotRoutes.Closing(Unaliased(TypeNameEmitter.Closed(tested))) is not { } constructed)
        {
            return slots;
        }

        foreach(var slot in SlotRoutes.ValueRouted(constructed.Definition))
        {
            if(SlotRoutes.Argument(constructed, slot) is (CapturedValue or TypeParameter) and var argument)
            {
                slots.Add((slot, argument));
            }
        }

        return slots;
    }

    internal static IMistType Unaliased(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Unaliased(aliased),
            RefinedType refined => Unaliased(refined.BaseType),
            _ => type,
        };
    }

    private static List<string> RuntimeComparisons(IMistType tested, string bound, DiagnosticBag diagnostics)
    {
        var comparisons = RuntimeSlots(tested).ConvertAll(runtime =>
            $"{bound}.{SlotRoutes.Hidden(runtime.Slot)} == {SlotValue(runtime.Argument, diagnostics)}");
        comparisons.AddRange(WitnessComparisons(tested, bound, diagnostics));
        return comparisons;
    }

    private static (IReadOnlyList<IMistType> Arguments, IReadOnlyList<TypeParameter> Parameters, Method Method)? CallTypeArguments(
        InvocationExpression invocation)
    {
        if(invocation.Method is not { } method)
        {
            return null;
        }

        var written = invocation.Receiver switch
        {
            MemberAccessExpression access => access.TypeArguments,
            MethodReferenceExpression reference => reference.TypeArguments,
            _ => [],
        };

        return CallTypeArguments(method, written);
    }

    private static (IReadOnlyList<IMistType> Arguments, IReadOnlyList<TypeParameter> Parameters, Method Method) CallTypeArguments(
        Method method, IReadOnlyList<IMistType> written)
    {
        var (arguments, parameters) = ExtensionBlockEmitter.CallTypeArguments(method, written);

        return (arguments, parameters ?? method.TypeParametersList, method);
    }

    private static IEnumerable<(TypeParameter Slot, IMistType? Argument)> RoutedCallSlots(
        IReadOnlyList<IMistType> arguments, IReadOnlyList<TypeParameter> parameters)
    {
        for(var i = 0; i < parameters.Count; i++)
        {
            if(SlotRoutes.IsValueRouted(parameters[i]))
            {
                yield return (parameters[i], i < arguments.Count ? TypeNameEmitter.Closed(arguments[i]) : null);
            }
        }
    }

    internal static List<string> CallSlotArguments(InvocationExpression invocation, DiagnosticBag diagnostics)
    {
        var hidden = new List<string>();

        if(CallTypeArguments(invocation) is not var (typeArguments, parameters, method))
        {
            return hidden;
        }

        hidden.AddRange(OwnerSlotArguments(method, invocation.Receiver is MemberAccessExpression access ? access.Receiver : null, diagnostics));

        foreach(var (slot, argument) in RoutedCallSlots(typeArguments, parameters))
        {
            hidden.Add(argument switch
            {
                CapturedValue captured when !invocation.Captures.Contains(captured.Capture) => ReadThroughReceiver(invocation, slot)
                    ?? ReadThroughArgument(invocation, captured)
                    ?? KeptLocal(captured.Capture)
                    ?? EmissionDiagnostics.Fallback("inferred slot value", $"{method.Name}.{slot.Name}", "default"),
                UnknownType or UnknownValueType => ReadThroughReceiver(invocation, slot) ?? Refuse(method, slot, Nesting(invocation, method, slot)),
                not null when SlotValue(argument, diagnostics) is { } value => value,
                _ => EmissionDiagnostics.Fallback("slot value", $"{method.Name}.{slot.Name}", "default"),
            });
        }

        foreach(var (_, argument) in WitnessedCallSlots(typeArguments, parameters))
        {
            hidden.Add(Witness(argument, diagnostics, captured => invocation.Captures.Contains(captured.Capture)
                ? null
                : ReadThroughArgument(invocation, captured) ?? KeptLocal(captured.Capture)));
        }

        return hidden;
    }

    private static IEnumerable<(TypeParameter Parameter, IMistType Argument)> WitnessedCallSlots(
        IReadOnlyList<IMistType> arguments, IReadOnlyList<TypeParameter> parameters)
    {
        for(var i = 0; i < parameters.Count && i < arguments.Count; i++)
        {
            if(!parameters[i].IsDependent && ClosingWitnesses.IsWitnessed(parameters[i]))
            {
                yield return (parameters[i], arguments[i]);
            }
        }
    }

    private static IEnumerable<CapturedValue> WitnessCaptures(IMistType argument)
    {
        var layout = new WitnessLayout();
        layout.Walk(argument);
        return layout.Parts.Select(part => part.Argument).OfType<CapturedValue>();
    }

    internal static bool CarriesSlots(FunctionReferenceExpression reference)
    {
        var (typeArguments, parameters, method) = CallTypeArguments(reference.Method, reference.TypeArguments);

        return RoutedCallSlots(typeArguments, parameters).Any()
            || WitnessedCallSlots(typeArguments, parameters).Any()
            || (method is { Modifiers.IsStatic: true, DeclaringType: MemberType owner } && SlotRoutes.Carried(owner).Count > 0);
    }

    internal static List<string> GroupSlotArguments(FunctionReferenceExpression reference, IReadOnlyList<string> arguments, string? receiver)
    {
        var (typeArguments, parameters, method) = CallTypeArguments(reference.Method, reference.TypeArguments);
        var hidden = OwnerSlotArguments(method, reference.Receiver, diagnostics: null);

        foreach(var (slot, argument) in RoutedCallSlots(typeArguments, parameters))
        {
            hidden.Add(argument switch
            {
                CapturedValue captured => ReadThroughBoundReceiver(method, slot, receiver)
                    ?? ReadThroughDelegateArgument(method, slot, arguments)
                    ?? evaluated?.GetValueOrDefault(captured.Capture)
                    ?? KeptLocal(captured.Capture)
                    ?? SlotValue(argument, diagnostics: null)
                    ?? EmissionDiagnostics.Fallback("inferred slot value", $"{method.Name}.{slot.Name}", "default"),
                UnknownType or UnknownValueType => ReadThroughBoundReceiver(method, slot, receiver)
                    ?? ReadThroughDelegateArgument(method, slot, arguments)
                    ?? Refuse(method, slot, Nesting(reference, slot)),
                not null when SlotValue(argument, diagnostics: null) is { } value => value,
                _ => EmissionDiagnostics.Fallback("slot value", $"{method.Name}.{slot.Name}", "default"),
            });
        }

        foreach(var (_, argument) in WitnessedCallSlots(typeArguments, parameters))
        {
            hidden.Add(Witness(argument, diagnostics: null, captured => evaluated?.GetValueOrDefault(captured.Capture) ?? KeptLocal(captured.Capture)));
        }

        return hidden;
    }

    private static List<string> OwnerSlotArguments(Method method, Expression? receiver, DiagnosticBag? diagnostics)
    {
        var hidden = new List<string>();

        if(method is not { Modifiers.IsStatic: true, DeclaringType: MemberType owner } || SlotRoutes.Carried(owner).Count == 0)
        {
            return hidden;
        }

        var closing = receiver switch
        {
            TypeReferenceExpression reference => Unaliased(reference.ReferencedType) as ConstructedType,
            null => SlotRoutes.ClosingFrom(enclosingType, owner),
            _ => null,
        };

        hidden.AddRange(closing is null ? HiddenReads(owner) : SlotValues(closing, diagnostics));

        return hidden;
    }

    internal static IEnumerable<string> HiddenReads(MemberType owner)
    {
        return SlotRoutes.Carried(owner).Select(slot => slot.IsDependent
            ? DependentCarrierNames.ValueRead(slot) ?? EmissionDiagnostics.Fallback("slot value", $"{owner.Name}.{slot.Name}", "default")
            : SlotReach.Read(slot));
    }

    internal static List<Expression> SlotBindings(InvocationExpression invocation)
    {
        var bindings = new List<Expression>();

        if(CallTypeArguments(invocation) is not var (typeArguments, parameters, method))
        {
            return bindings;
        }

        var carriers = new List<Expression>();

        foreach(var (slot, argument) in RoutedCallSlots(typeArguments, parameters))
        {
            if(Carrier(invocation, slot, argument) is { } carrier && !carriers.Contains(carrier))
            {
                carriers.Add(carrier);
            }
        }

        foreach(var captured in WitnessedCallSlots(typeArguments, parameters).SelectMany(witnessed => WitnessCaptures(witnessed.Argument)))
        {
            if(!invocation.Captures.Contains(captured.Capture) && CarryingArgument(invocation, captured)?.Argument is { } carrier && !carriers.Contains(carrier))
            {
                carriers.Add(carrier);
            }
        }

        if(carriers.Count == 0)
        {
            return bindings;
        }

        var held = method.Modifiers.IsStatic ? null : (invocation.Receiver as MemberAccessExpression)?.Receiver;

        if(held is not (null or TypeReferenceExpression))
        {
            bindings.AddRange(carriers.Contains(held) && RunsInPlace(held) ? [held] : BoundAhead(held, Held(held)));
        }

        var last = carriers.Max(carrier => invocation.Arguments.ToList().FindIndex(argument => ReferenceEquals(argument, carrier)));

        for(var i = 0; i <= last; i++)
        {
            var byReference = invocation.Parameters is { } declared && i < declared.Count && ArgumentKeywords.For(declared[i]).Length > 0;

            bindings.AddRange(BoundAhead(invocation.Arguments[i], byReference ? Reading.Reference : Reading.Value).Where(part => !bindings.Contains(part)));
        }

        foreach(var carrier in carriers)
        {
            if(!ReferenceEquals(carrier, held) && !IsPlainRead(carrier) && !bindings.Contains(carrier) && boundParts?.ContainsKey(carrier) != true)
            {
                bindings.Add(carrier);
            }
        }

        return bindings;
    }

    private static string? ReadThroughReceiver(InvocationExpression invocation, TypeParameter slot)
    {
        return ReceiverRead(invocation, slot) is var (receiver, member) ? $"{RenderAt(receiver, PostfixPrecedence, new DiagnosticBag())}.{member}" : null;
    }

    private static string? ReadThroughArgument(InvocationExpression invocation, CapturedValue captured)
    {
        if(CarryingArgument(invocation, captured) is not var (argument, closing, slot))
        {
            return null;
        }

        var member = LengthErasure.IsLengthIndexed(closing) ? "Length" : SlotRoutes.Hidden(slot);
        return $"{RenderAt(argument, PostfixPrecedence, new DiagnosticBag())}.{member}";
    }

    private static (Expression Argument, ConstructedType Closing, TypeParameter Slot)? CarryingArgument(
        InvocationExpression invocation, CapturedValue captured)
    {
        for(var i = 0; i < invocation.Arguments.Count; i++)
        {
            var closed = invocation.ParameterTypes is { } types && i < types.Count ? types[i] : invocation.Arguments[i].Type;

            if(Unaliased(TypeNameEmitter.Closed(closed)) is not ConstructedType closing || closing.Definition is not IHasTypeParameters generic)
            {
                continue;
            }

            foreach(var slot in generic.TypeParametersList)
            {
                if(slot.Ordinal < closing.TypeArguments.Count
                    && ReferenceEquals(closing.TypeArguments[slot.Ordinal], captured)
                    && (LengthErasure.IsLengthIndexed(closing) || SlotRoutes.IsValueRouted(slot)))
                {
                    return (invocation.Arguments[i], closing, slot);
                }
            }
        }

        return null;
    }

    private static string? ReadThroughBoundReceiver(Method method, TypeParameter slot, string? receiver)
    {
        return receiver is not null && ReceiverMember(method, slot) is { } member ? $"{receiver}.{member}" : null;
    }

    private static string? ReadThroughDelegateArgument(Method method, TypeParameter slot, IReadOnlyList<string> arguments)
    {
        return CarryingParameter(method, slot) is var (ordinal, closing, carried) && ordinal < arguments.Count
            ? $"{arguments[ordinal]}.{CarriedMember(closing, carried)}"
            : null;
    }

    internal static string? OperandRead(Method method, TypeParameter slot)
    {
        return CarryingParameter(method, slot) is var (ordinal, closing, carried)
            ? $"{IdentifierSpelling.Of(method.ParametersList[ordinal].Name)}.{CarriedMember(closing, carried)}"
            : null;
    }

    private static string CarriedMember(ConstructedType closing, TypeParameter carried)
    {
        return LengthErasure.IsLengthIndexed(closing) ? "Length" : SlotRoutes.Hidden(carried);
    }

    private static (int Ordinal, ConstructedType Closing, TypeParameter Carried)? CarryingParameter(Method method, TypeParameter slot)
    {
        for(var i = 0; i < method.ParametersList.Count; i++)
        {
            var type = Unaliased(TypeNameEmitter.Closed(method.ParametersList[i].Type));

            if(type is MemberType nested && SlotRoutes.ValueRouted(nested).FirstOrDefault(carried => ReferenceEquals(SlotRoutes.Argument(nested, carried), slot)) is { } own)
            {
                return (i, new ConstructedType(nested, nested.TypeParametersList), own);
            }

            if(type is not ConstructedType closing || closing.Definition is not IHasTypeParameters generic)
            {
                continue;
            }

            for(var k = 0; k < closing.TypeArguments.Count && k < generic.TypeParametersList.Count; k++)
            {
                if(ReferenceEquals(closing.TypeArguments[k], slot)
                    && (LengthErasure.IsLengthIndexed(closing) || SlotRoutes.IsValueRouted(generic.TypeParametersList[k]) || ClosingWitnesses.IsWitnessed(generic.TypeParametersList[k])))
                {
                    return (i, closing, generic.TypeParametersList[k]);
                }
            }

            if(SlotRoutes.ValueRouted(closing).FirstOrDefault(carried => ReferenceEquals(SlotRoutes.Argument(closing, carried), slot)) is { } enclosing)
            {
                return (i, closing, enclosing);
            }
        }

        return null;
    }

    private static IMistType? Nesting(FunctionReferenceExpression reference, TypeParameter slot)
    {
        var method = reference.Method;

        if(BlockNesting(method, slot) is { } receiverType)
        {
            return receiverType;
        }

        return method.ParametersList.Select(parameter => parameter.Type).FirstOrDefault(type => Mentions(type, slot)) is { } nested
            ? TypeSubstitution.ForMethod(method, reference.TypeArguments).Apply(nested, interner: null)
            : null;
    }

    private static IMistType? Nesting(InvocationExpression invocation, Method method, TypeParameter slot)
    {
        if(BlockNesting(method, slot) is { } receiverType)
        {
            return receiverType;
        }

        for(var i = 0; i < method.ParametersList.Count && i < invocation.Arguments.Count; i++)
        {
            if(Mentions(method.ParametersList[i].Type, slot))
            {
                return invocation.ParameterTypes is { } types && i < types.Count ? types[i] : invocation.Arguments[i].Type;
            }
        }

        return null;
    }

    private static IMistType? BlockNesting(Method method, TypeParameter slot)
    {
        return method.ClosedBlock is { ResolvedReceiverType: { } receiverType } block && block.TypeParametersList.Contains(slot)
            ? method.BlockClosing.Apply(receiverType, interner: null)
            : null;
    }

    private static string Refuse(Method method, TypeParameter slot, IMistType? nesting)
    {
        if(nesting is null)
        {
            return EmissionDiagnostics.Fallback("inferred slot value", $"{method.Name}.{slot.Name}", "default");
        }

        EmissionDiagnostics.Refuse(
            BackendDiagnostics.InferredSlotNestedInArgument, new NameValue(method.Name), new NameValue(slot.Name), DiagnosticSnapshot.Type(nesting, TypeDisplay.Mist(Unknowing(nesting))));
        return "default";
    }

    private static IMistType Unknowing(IMistType type)
    {
        return type is ConstructedType constructed
            ? new ConstructedType(
                constructed.Definition,
                [.. constructed.TypeArguments.Select(argument => argument is UnknownValueType ? UnknownType.Instance : Unknowing(argument))],
                constructed.Enclosing is { } enclosing ? Unknowing(enclosing) as ConstructedType : null)
            : type;
    }

    private static bool Mentions(IMistType type, IMistType target)
    {
        var closed = TypeNameEmitter.Closed(type);

        return ReferenceEquals(closed, target) || (Unaliased(closed) is ConstructedType constructed && constructed.TypeArguments.Any(argument => Mentions(argument, target)));
    }

    private static (Expression Receiver, string Member)? ReceiverRead(InvocationExpression invocation, TypeParameter slot)
    {
        return invocation is { Receiver: MemberAccessExpression { Receiver: var receiver and not TypeReferenceExpression }, Method: { } method }
            && ReceiverMember(method, slot) is { } member
                ? (receiver, member)
                : null;
    }

    private static string? ReceiverMember(Method method, TypeParameter slot)
    {
        if(method is not { Modifiers.IsStatic: false, ClosedBlock: { } block } || SlotRoutes.ReceiverSlot(block, slot) is not var (open, carried))
        {
            return null;
        }

        return LengthErasure.IsLengthIndexed(open) ? "Length"
            : SlotRoutes.IsValueRouted(carried) ? SlotRoutes.Hidden(carried)
            : null;
    }

    private static Expression? Carrier(InvocationExpression invocation, TypeParameter slot, IMistType? argument)
    {
        return argument switch
        {
            CapturedValue captured when invocation.Captures.Contains(captured.Capture) => (captured.Capture.Initializer as MemberAccessExpression)?.Receiver,
            CapturedValue captured => ReceiverRead(invocation, slot)?.Receiver ?? CarryingArgument(invocation, captured)?.Argument ?? Owning(invocation, captured.Capture),
            UnknownType or UnknownValueType => ReceiverRead(invocation, slot)?.Receiver,
            _ => null,
        };
    }

    private static Expression? Owning(InvocationExpression invocation, SlotCapture capture)
    {
        var parts = invocation.Receiver is MemberAccessExpression { Receiver: var receiver and not TypeReferenceExpression }
            ? invocation.Arguments.Prepend(receiver)
            : invocation.Arguments;

        return KeptLocal(capture) is null
            ? null
            : parts.FirstOrDefault(part => BodyWalker.DescendantsAndSelf(part).Any(node => SlotCapture.Of(node).Contains(capture)));
    }

    private static void EmitBoundCall(List<Expression> bindings, Action emit, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var precedence = bindings.Count == 1 ? SwitchPrecedence + 1 : AssignmentPrecedence;
        var parts = bindings.Select(part => (Rendered: RenderAt(part, precedence, diagnostics), Name: TemporaryName())).ToList();
        var enclosing = boundParts;
        boundParts = new Dictionary<Expression, string>(enclosing ?? [], ReferenceEqualityComparer.Instance);

        try
        {
            for(var i = 0; i < bindings.Count; i++)
            {
                boundParts[bindings[i]] = parts[i].Name;
            }

            writer.Append(parts.Count == 1
                ? $"({parts[0].Rendered} switch {{ var {parts[0].Name} => "
                : $"(({string.Join(", ", parts.Select(part => part.Rendered))}) switch {{ ({string.Join(", ", parts.Select(part => "var " + part.Name))}) => ");
            emit();
            writer.Append(" })");
        }
        finally
        {
            boundParts = enclosing;
        }
    }

    internal static bool IsPlainRead(Expression expression)
    {
        return expression switch
        {
            ImplicitConversionExpression conversion => IsPlainRead(conversion.Operand),
            MemberAccessExpression { Member: FieldResult } access => IsPlainRead(access.Receiver),
            _ => InstanceSlotReads.IsPlainRead(expression),
        };
    }
}