using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
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
/// it twice. An extension block's own parameter is read through the receiver that closed it, as its
/// length or its hidden field. The hidden argument comes first in the C# call, so the receiver and every
/// argument written before the carrier are bound with it, in source order, or they would run after it. A
/// method group has no argument yet, so its lambda reads each slot from the delegate's parameter that
/// carries it, or from the receiver bound when the group was formed.
/// </summary>
internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static NewExpression? discarding;

    internal static string? SlotValue(IMistType argument, DiagnosticBag? diagnostics)
    {
        return TypeNameEmitter.Closed(argument) switch
        {
            LiftedType lifted => DependentCarrierNames.Literal(lifted.Value),
            TypeParameter parameter => DependentCarrierNames.ValueRead(parameter),
            CapturedValue captured => Render(captured.Capture.Initializer, diagnostics ?? new DiagnosticBag()),
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
            && parameter.Ordinal < closing.TypeArguments.Count
                ? SlotValue(closing.TypeArguments[parameter.Ordinal], diagnostics)
                    ?? EmissionDiagnostics.Fallback("slot value", $"{TypeDisplay.Mist(closing)}.{parameter.Name}", "default")
                : null;
    }

    internal static List<string> SlotValues(IMistType? closing, DiagnosticBag? diagnostics, string? receiver = null)
    {
        var values = new List<string>();

        if(closing is not ConstructedType constructed)
        {
            return values;
        }

        foreach(var slot in SlotRoutes.ValueRouted(constructed.Definition))
        {
            var argument = slot.Ordinal < constructed.TypeArguments.Count ? constructed.TypeArguments[slot.Ordinal] : null;
            values.Add(argument switch
            {
                null => EmissionDiagnostics.Fallback("slot value", slot.Name, "default"),
                _ when receiver is not null && TypeNameEmitter.Closed(argument) is TypeParameter carried && SlotRoutes.IsValueRouted(carried)
                    => $"{receiver}.{SlotRoutes.Hidden(carried)}",
                _ => SlotValue(argument, diagnostics) ?? EmissionDiagnostics.Fallback("slot value", $"{TypeDisplay.Mist(constructed)}.{slot.Name}", "default"),
            });
        }

        return values;
    }

    internal static List<(TypeParameter Slot, IMistType Argument)> RuntimeSlots(IMistType tested)
    {
        var slots = new List<(TypeParameter, IMistType)>();

        if(Unaliased(TypeNameEmitter.Closed(tested)) is not ConstructedType constructed)
        {
            return slots;
        }

        foreach(var slot in SlotRoutes.ValueRouted(constructed.Definition))
        {
            if(slot.Ordinal < constructed.TypeArguments.Count && constructed.TypeArguments[slot.Ordinal] is CapturedValue or TypeParameter)
            {
                slots.Add((slot, constructed.TypeArguments[slot.Ordinal]));
            }
        }

        return slots;
    }

    private static IMistType Unaliased(IMistType type)
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
        return RuntimeSlots(tested).ConvertAll(runtime =>
            $"{bound}.{SlotRoutes.Hidden(runtime.Slot)} == {SlotValue(runtime.Argument, diagnostics)}");
    }

    private static void RefuseZeroedStorage(IMistType element, string storage, Storm.Runtime.SyntaxNode node, DiagnosticBag diagnostics)
    {
        if(Unaliased(TypeNameEmitter.Closed(element)) is ConstructedType { Definition: StructType } constructed
            && SlotRoutes.ValueRouted(constructed.Definition) is [var slot, ..])
        {
            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.ZeroedStorageOfValueRoutedStruct, node, null,
                DiagnosticSnapshot.Type(constructed), new EnumValue(storage), new NameValue(slot.Name));
        }
    }

    private static void RefuseCombinedRuntimeTest(IMistType tested, Storm.Runtime.SyntaxNode node, DiagnosticBag diagnostics)
    {
        if(RuntimeSlots(tested) is [var (slot, _), ..])
        {
            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.RuntimeClosingTestUnderCombinator, node, null, DiagnosticSnapshot.Type(tested), new NameValue(slot.Name));
        }
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
                    ?? RefuseNested(invocation, method, slot, captured),
                UnknownType or UnknownValueType => ReadThroughReceiver(invocation, slot) ?? RefuseNested(invocation, method, slot, carried: null),
                not null when SlotValue(argument, diagnostics) is { } value => value,
                _ => EmissionDiagnostics.Fallback("slot value", $"{method.Name}.{slot.Name}", "default"),
            });
        }

        return hidden;
    }

    internal static bool CarriesSlots(FunctionReferenceExpression reference)
    {
        var (typeArguments, parameters, method) = CallTypeArguments(reference.Method, reference.TypeArguments);

        return RoutedCallSlots(typeArguments, parameters).Any()
            || (method is { Modifiers.IsStatic: true, DeclaringType: MemberType owner } && SlotRoutes.ValueRouted(owner).Count > 0);
    }

    internal static List<string> GroupSlotArguments(FunctionReferenceExpression reference, IReadOnlyList<string> arguments, string? receiver)
    {
        var (typeArguments, parameters, method) = CallTypeArguments(reference.Method, reference.TypeArguments);
        var hidden = OwnerSlotArguments(method, reference.Receiver, diagnostics: null);

        foreach(var (slot, argument) in RoutedCallSlots(typeArguments, parameters))
        {
            hidden.Add(argument switch
            {
                CapturedValue or UnknownType or UnknownValueType => ReadThroughBoundReceiver(method, slot, receiver)
                    ?? ReadThroughDelegateArgument(method, slot, arguments)
                    ?? (Nesting(reference, slot) is { } nesting ? Refuse(method, slot, nesting) : SlotValue(argument, diagnostics: null))
                    ?? EmissionDiagnostics.Fallback("inferred slot value", $"{method.Name}.{slot.Name}", "default"),
                not null when SlotValue(argument, diagnostics: null) is { } value => value,
                _ => EmissionDiagnostics.Fallback("slot value", $"{method.Name}.{slot.Name}", "default"),
            });
        }

        return hidden;
    }

    private static List<string> OwnerSlotArguments(Method method, Expression? receiver, DiagnosticBag? diagnostics)
    {
        var hidden = new List<string>();

        if(method is not { Modifiers.IsStatic: true, DeclaringType: MemberType owner } || SlotRoutes.ValueRouted(owner).Count == 0)
        {
            return hidden;
        }

        var closing = receiver switch
        {
            TypeReferenceExpression reference => Unaliased(reference.ReferencedType) as ConstructedType,
            null => SlotRoutes.ClosingFrom(enclosingType, owner),
            _ => null,
        };

        hidden.AddRange(closing is null
            ? SlotRoutes.ValueRouted(owner).Select(slot => EmissionDiagnostics.Fallback("slot value", $"{owner.Name}.{slot.Name}", "default"))
            : SlotValues(closing, diagnostics));

        return hidden;
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

        if(carriers.Count == 0)
        {
            return bindings;
        }

        if(invocation.Receiver is MemberAccessExpression { Receiver: var receiver and not TypeReferenceExpression }
            && !method.Modifiers.IsStatic
            && !IsPlainRead(receiver))
        {
            bindings.Add(receiver);
        }

        var last = carriers.Max(carrier => invocation.Arguments.ToList().FindIndex(argument => ReferenceEquals(argument, carrier)));

        for(var i = 0; i <= last; i++)
        {
            var argument = invocation.Arguments[i];
            var byReference = invocation.Parameters is { } declared && i < declared.Count && ArgumentKeywords.For(declared[i]).Length > 0;

            if(!IsPlainRead(argument) && !byReference && !bindings.Contains(argument))
            {
                bindings.Add(argument);
            }
        }

        foreach(var carrier in carriers)
        {
            if(!IsPlainRead(carrier) && !bindings.Contains(carrier))
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
            ? $"{arguments[ordinal]}.{(LengthErasure.IsLengthIndexed(closing) ? "Length" : SlotRoutes.Hidden(carried))}"
            : null;
    }

    private static (int Ordinal, ConstructedType Closing, TypeParameter Carried)? CarryingParameter(Method method, TypeParameter slot)
    {
        for(var i = 0; i < method.ParametersList.Count; i++)
        {
            if(Unaliased(TypeNameEmitter.Closed(method.ParametersList[i].Type)) is not ConstructedType closing || closing.Definition is not IHasTypeParameters generic)
            {
                continue;
            }

            for(var k = 0; k < closing.TypeArguments.Count && k < generic.TypeParametersList.Count; k++)
            {
                if(ReferenceEquals(closing.TypeArguments[k], slot)
                    && (LengthErasure.IsLengthIndexed(closing) || SlotRoutes.IsValueRouted(generic.TypeParametersList[k])))
                {
                    return (i, closing, generic.TypeParametersList[k]);
                }
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

    private static IMistType? BlockNesting(Method method, TypeParameter slot)
    {
        return method.ClosedBlock is { ResolvedReceiverType: { } receiverType } block && block.TypeParametersList.Contains(slot)
            ? method.BlockClosing.Apply(receiverType, interner: null)
            : null;
    }

    private static string RefuseNested(InvocationExpression invocation, Method method, TypeParameter slot, CapturedValue? carried)
    {
        return Refuse(method, slot, BlockNesting(method, slot)
            ?? (carried is null ? null : (invocation.ParameterTypes ?? []).FirstOrDefault(type => Mentions(type, carried))));
    }

    private static string Refuse(Method method, TypeParameter slot, IMistType? nesting)
    {
        if(nesting is null)
        {
            return EmissionDiagnostics.Fallback("inferred slot value", $"{method.Name}.{slot.Name}", "default");
        }

        EmissionDiagnostics.Refuse(BackendDiagnostics.InferredSlotNestedInArgument, new NameValue(method.Name), new NameValue(slot.Name), DiagnosticSnapshot.Type(nesting));
        return "default";
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
            CapturedValue captured => ReceiverRead(invocation, slot)?.Receiver ?? CarryingArgument(invocation, captured)?.Argument,
            UnknownType or UnknownValueType => ReceiverRead(invocation, slot)?.Receiver,
            _ => null,
        };
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

    private static List<Expression> Discarded(IEnumerable<SlotCapture> captures)
    {
        return [.. captures.Select(capture => capture.Initializer).Where(initializer => !IsPlainRead(initializer))];
    }

    private static bool ConsumesCapture(Expression? initializer, SlotCapture capture)
    {
        var inner = initializer is ImplicitConversionExpression conversion ? conversion.Operand : initializer;

        return inner is DefaultExpression defaulted
            && Unaliased(TypeNameEmitter.Closed(defaulted.Type)) is ConstructedType constructed
            && SlotRoutes.ValueRouted(constructed.Definition).Any(slot =>
                slot.Ordinal < constructed.TypeArguments.Count
                && constructed.TypeArguments[slot.Ordinal] is CapturedValue captured
                && ReferenceEquals(captured.Capture, capture));
    }

    private static string WrapDiscarded(IEnumerable<SlotCapture> captures, string inner, DiagnosticBag diagnostics)
    {
        foreach(var initializer in Discarded(captures).AsEnumerable().Reverse())
        {
            inner = $"({RenderAt(initializer, SwitchPrecedence + 1, diagnostics)} switch {{ _ => {inner} }})";
        }

        return inner;
    }

    private static IEnumerable<SlotCapture> Unconsumed(NewExpression newExpression)
    {
        var consumed = Unaliased(TypeNameEmitter.Closed(newExpression.Type)) is ConstructedType constructed
            ? SlotRoutes.ValueRouted(constructed.Definition)
                .Select(slot => slot.Ordinal < constructed.TypeArguments.Count ? constructed.TypeArguments[slot.Ordinal] as CapturedValue : null)
                .OfType<CapturedValue>()
                .Select(captured => captured.Capture)
                .ToList()
            : [];

        return newExpression.Captures.Where(capture => !consumed.Contains(capture));
    }
}