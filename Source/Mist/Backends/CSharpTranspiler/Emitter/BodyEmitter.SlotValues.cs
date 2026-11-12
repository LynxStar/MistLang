using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
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
/// it twice.
/// </summary>
internal static partial class BodyEmitter
{
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

    internal static List<string> SlotValues(IMistType? closing, DiagnosticBag? diagnostics)
    {
        var values = new List<string>();

        if(closing is not ConstructedType constructed)
        {
            return values;
        }

        foreach(var slot in SlotRoutes.ValueRouted(constructed.Definition))
        {
            var argument = slot.Ordinal < constructed.TypeArguments.Count ? constructed.TypeArguments[slot.Ordinal] : null;
            values.Add(argument is null ? EmissionDiagnostics.Fallback("slot value", slot.Name, "default") : SlotValue(argument, diagnostics)
                ?? EmissionDiagnostics.Fallback("slot value", $"{TypeDisplay.Mist(constructed)}.{slot.Name}", "default"));
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

    private static void RefuseCombinedRuntimeTest(IMistType tested, Storm.Runtime.SyntaxNode node, DiagnosticBag diagnostics)
    {
        if(RuntimeSlots(tested) is [var (slot, _), ..])
        {
            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.RuntimeClosingTestUnderCombinator, node, null, DiagnosticSnapshot.Type(tested), new NameValue(slot.Name));
        }
    }

    private static (IReadOnlyList<IMistType> Arguments, Method? Method) CallTypeArguments(InvocationExpression invocation)
    {
        return invocation.Receiver switch
        {
            MemberAccessExpression access => (access.TypeArguments, invocation.Method),
            MethodReferenceExpression reference => (reference.TypeArguments, invocation.Method),
            _ => ([], invocation.Method),
        };
    }

    internal static List<string> CallSlotArguments(InvocationExpression invocation, DiagnosticBag diagnostics)
    {
        var hidden = new List<string>();
        var (typeArguments, method) = CallTypeArguments(invocation);

        if(method is null)
        {
            return hidden;
        }

        if(method is { Modifiers.IsStatic: true, DeclaringType: MemberType owner } && SlotRoutes.ValueRouted(owner).Count > 0)
        {
            var closing = invocation.Receiver switch
            {
                MemberAccessExpression { Receiver: TypeReferenceExpression reference } => Unaliased(reference.ReferencedType) as ConstructedType,
                MethodReferenceExpression => SlotRoutes.ClosingFrom(enclosingType, owner),
                _ => null,
            };

            hidden.AddRange(closing is null
                ? SlotRoutes.ValueRouted(owner).Select(slot => EmissionDiagnostics.Fallback("slot value", $"{owner.Name}.{slot.Name}", "default"))
                : SlotValues(closing, diagnostics));
        }

        foreach(var slot in SlotRoutes.ValueRouted(method.TypeParametersList))
        {
            var argument = slot.Ordinal < typeArguments.Count ? TypeNameEmitter.Closed(typeArguments[slot.Ordinal]) : null;

            hidden.Add(argument switch
            {
                CapturedValue captured when !invocation.Captures.Contains(captured.Capture) => ReadThroughArgument(invocation, captured)
                    ?? EmissionDiagnostics.Fallback("inferred slot value", $"{method.Name}.{slot.Name}", "default"),
                not null when SlotValue(argument, diagnostics) is { } value => value,
                _ => EmissionDiagnostics.Fallback("slot value", $"{method.Name}.{slot.Name}", "default"),
            });
        }

        return hidden;
    }

    internal static List<Expression> SlotBindings(InvocationExpression invocation)
    {
        var bindings = new List<Expression>();
        var (typeArguments, method) = CallTypeArguments(invocation);

        if(method is null)
        {
            return bindings;
        }

        foreach(var slot in SlotRoutes.ValueRouted(method.TypeParametersList))
        {
            if(slot.Ordinal >= typeArguments.Count || TypeNameEmitter.Closed(typeArguments[slot.Ordinal]) is not CapturedValue captured)
            {
                continue;
            }

            var carrier = invocation.Captures.Contains(captured.Capture)
                ? (captured.Capture.Initializer as MemberAccessExpression)?.Receiver
                : CarryingArgument(invocation, captured)?.Argument;

            if(carrier is not null && !IsPlainRead(carrier) && !bindings.Contains(carrier))
            {
                bindings.Add(carrier);
            }
        }

        return bindings;
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