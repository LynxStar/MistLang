using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// What a value or a reference may outlive, answered as the frame storage it refers to -- stackalloc
/// memory, a local, a parameter passed by value, a struct member's receiver -- or null when nothing ties
/// it to the frame; and where a stored ref struct would outlive the frame, with why. The scopes are C#'s
/// ref-safety contexts, which the emitted program is judged by: a call's ref struct result is as narrow as
/// the narrowest ref struct it is given or reference it is lent, and a ref struct local keeps the scope its
/// declaration gave it whatever is assigned later, which is why a local declared from a heap value, or
/// from nothing, may not take a stackalloc-backed one. A parameter's value belongs to its caller.
/// </summary>
internal static class FrameScopes
{
    public const string StackMemory = "stackalloc memory";

    public static string? Value(Expression expression, IReadOnlyDictionary<LocalVariable, LocalFrame> locals)
    {
        return expression switch
        {
            StackallocExpression => StackMemory,
            LocalReferenceExpression { Local: var local } => locals.TryGetValue(local, out var frame) ? frame.Value : null,
            MemberAccessExpression { Member: FieldResult { Field.Modifiers.IsStatic: false } or PropertyResult { Property.Modifiers.IsStatic: false } } access
                => Held(access.Receiver, locals),
            ExtensionPropertyAccessExpression extension => Held(extension.Receiver, locals),
            InvocationExpression call when RefStructs.Is(call.Type)
                => Narrowest([Held(ThisTiers.ReceiverOf(call), locals), .. Lent(call.Arguments, call.Parameters, locals)]),
            NewExpression creation when RefStructs.Is(creation.Type)
                => Narrowest([.. Lent(creation.Arguments, creation.Constructor?.ParametersList, locals),
                    .. creation.Initializer.Select(entry => Held(entry.Value, locals))]),
            IndexExpression index when RefStructs.Is(index.Type) => Narrowest([Held(index.Receiver, locals), Held(index.Index, locals)]),
            ImplicitConversionExpression conversion => Value(conversion.Operand, locals),
            CastExpression cast => Value(cast.Operand, locals),
            AsExpression converted => Value(converted.Operand, locals),
            SwitchExpression switched => Narrowest([.. switched.Arms.Select(arm => Value(arm.Result, locals))]),
            CoalesceExpression coalesce => Narrowest([Value(coalesce.Left, locals), Value(coalesce.Right, locals)]),
            AssignmentExpression { IsCoalescing: false } assignment => Value(assignment.Value, locals),
            _ => null,
        };
    }

    public static string? Referent(Expression expression, IReadOnlyDictionary<LocalVariable, LocalFrame> locals)
    {
        return expression switch
        {
            LocalReferenceExpression { Local: { Parameter: { } parameter } local } => parameter.Modifier.IsRef ? null : $"parameter '{local.Name}'",
            LocalReferenceExpression { Local: { IsRef: true } local } => locals.TryGetValue(local, out var frame) ? frame.Referent : null,
            LocalReferenceExpression { Local: var local } => $"local '{local.Name}'",
            ThisExpression self => ThisTiers.IsValue(self.Type) ? Receiver(self.Type) : "'this'",
            FieldReferenceExpression { Field: { Modifiers.IsStatic: false, DeclaringType: { } owner } } when ThisTiers.IsValue(owner) => Receiver(owner),
            BackingFieldReferenceExpression { Property: { Modifiers.IsStatic: false, DeclaringType: { } owner } } when ThisTiers.IsValue(owner) => Receiver(owner),
            MemberAccessExpression { Member: FieldResult { Field.Modifiers.IsStatic: false } } access
                when ThisTiers.IsValue(Requirement.Unrefined(access.Receiver.Type)) => Referent(access.Receiver, locals),
            IndexExpression index when IsSpan(index.Receiver.Type) => Value(index.Receiver, locals),
            IndexExpression { Indexer: { Modifiers.IsRef: true } indexer } index
                => Narrowest([Held(index.Receiver, locals), .. Lent([index.Index], indexer.ParametersList, locals)]),
            InvocationExpression { Signature.Modifiers.IsRef: true } call
                => Narrowest([Held(ThisTiers.ReceiverOf(call), locals), .. Lent(call.Arguments, call.Parameters, locals)]),
            MemberAccessExpression { Member: PropertyResult { Property.Modifiers.IsRef: true } } access => Held(access.Receiver, locals),
            ImplicitConversionExpression { Operator: null } conversion => Referent(conversion.Operand, locals),
            _ => null,
        };
    }

    public static (string Where, string Why)? Destination(Expression target, IReadOnlyDictionary<LocalVariable, LocalFrame> locals)
    {
        return target switch
        {
            LocalReferenceExpression { Local: { Parameter: not null } local } => ($"parameter '{local.Name}'", "a parameter's value belongs to the caller"),
            LocalReferenceExpression { Local: var local } when locals.TryGetValue(local, out var frame) && frame.Value is not null => null,
            LocalReferenceExpression { Local: { IsRef: true } local }
                => ($"the storage '{local.Name}' refers to", "that storage was not declared from a stack-bound value, so what it holds may leave the method"),
            LocalReferenceExpression { Local: var local }
                => ($"local '{local.Name}'", $"'{local.Name}' was not declared from a stack-bound value, so what it holds may leave the method"),
            ThisExpression => ("'this'", "the receiver belongs to the caller"),
            FieldReferenceExpression { Field: { Modifiers.IsStatic: false } field } => ($"field '{field.Name}' of 'this'", "the receiver belongs to the caller"),
            MemberAccessExpression { Member: FieldResult { Field: { Modifiers.IsStatic: false } field } } access
                when RefStructs.Is(access.Receiver.Type) && Destination(access.Receiver, locals) is { } holder
                => ($"field '{field.Name}' of {holder.Where}", holder.Why),
            MemberAccessExpression { Member: PropertyResult { Property: { Modifiers.IsStatic: false } property } } access
                when RefStructs.Is(access.Receiver.Type) && Destination(access.Receiver, locals) is { } holder
                => ($"property '{property.Name}' of {holder.Where}", holder.Why),
            _ => null,
        };
    }

    private static string? Held(Expression? value, IReadOnlyDictionary<LocalVariable, LocalFrame> locals)
    {
        return value is not null && RefStructs.Is(value.Type) ? Value(value, locals) : null;
    }

    private static IEnumerable<string?> Lent(
        IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, IReadOnlyDictionary<LocalVariable, LocalFrame> locals)
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(parameters is not null && i < parameters.Count && parameters[i].Modifier.IsRef)
            {
                yield return Referent(arguments[i], locals);
            }

            yield return Held(arguments[i], locals);
        }
    }

    private static string? Narrowest(IReadOnlyList<string?> origins)
    {
        return origins.FirstOrDefault(origin => origin is not null);
    }

    private static string Receiver(IMistType owner)
    {
        return $"the receiver 'this' of struct '{TypeDisplay.Mist(owner)}'";
    }

    private static bool IsSpan(IMistType type)
    {
        return Requirement.Unrefined(type) is ConstructedType { Definition.FullyQualifiedName: var name }
            && (name == WellKnownTypes.Span.FullyQualifiedName || name == WellKnownTypes.ReadOnlySpan.FullyQualifiedName);
    }
}