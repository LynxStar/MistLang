using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A method group whose call <see cref="IntrinsicCalls"/> or <see cref="InvariantCulture"/> spells differently
/// from the CLR member of the same name: written bare, Roslyn binds the CLR group, the culture-reading overload or
/// one that does not exist. It becomes a lambda over the respelled call instead. The receiver is passed in rather
/// than captured by name, so it is evaluated once where the group is formed and later writes to the variable do
/// not reach the function, as with a method group.
/// </summary>
internal static class RespelledGroups
{
    private const string Bound = "__receiver";

    public static bool Applies(FunctionReferenceExpression reference)
    {
        return reference.Method.ParametersList.Any(parameter => ValuePack.PackParameterOf(parameter) is not null)
            || AsyncPromotion.IsPromoted(reference.Method)
            || DefaultWitnesses.StaticParameters(reference.Method).Count > 0
            || reference.Method.TypeParametersList.Any(DefaultConstraintWitness.Constrains)
            || ConstructorWitnesses.Of(reference.Method.TypeParametersList).Count > 0
            || ContextKeyWitnesses.Ordinals(reference.Method).Count > 0
            || (reference.Method is { Modifiers.IsStatic: true, DeclaringType: ConcreteType owner }
                && ConstructorWitnesses.Stored(owner).Count > 0)
            || ExtensionCalls.Carrier(reference.Method) is not null
            || IntrinsicCalls.Respells(reference.Method)
            || (InvariantCulture.FormatsItself(reference.Method) && reference.Receiver is { } receiver && !InvariantCulture.Fixes(receiver.Type));
    }

    public static string Lambda(FunctionReferenceExpression reference, string? receiver)
    {
        var method = reference.Method;
        var function = TypeNameEmitter.Emit(reference.Type);
        var arguments = Enumerable.Range(0, ((IFunctionType)reference.Type).ParameterTypes.Count).Select(i => $"__arg{i}").ToList();
        var receiverType = reference.Receiver?.Type;
        if(receiver is null && !method.Modifiers.IsStatic && method.DeclaringType is StructType owner)
        {
            receiver = ReceiverSpelling.This;
            receiverType = OpenOwner(owner);
        }

        var captured = receiver is null ? null : Bound;
        var callArguments = DefaultWitnesses.MethodArguments(method, reference.TypeArguments, receiverType ?? method.DeclaringType, reference.ContextTypes)
            .Select(argument => argument ?? "/* unsupported: default constraint */").Concat(PackArguments(reference, arguments));
        var target = captured ?? (method.Modifiers.IsStatic
            ? method.DeclaringType is { } declaring
                ? TypeNameEmitter.Emit(receiverType is null ? declaring : UnsafeReach.ClosedOwner(declaring, receiverType))
                : null
            : ReceiverSpelling.This);
        var carrier = ExtensionCalls.Carrier(method);
        if(carrier is not null)
        {
            target = carrier;
            if(!method.Modifiers.IsStatic)
            {
                callArguments = new[] { captured ?? ReceiverSpelling.This }.Concat(callArguments);
            }
        }

        var call = IntrinsicCalls.Respells(method)
            ? IntrinsicCalls.Rewrite(method, captured, receiverType, arguments)
            : carrier is null && InvariantCulture.FormatsItself(method) && receiverType is not null && !InvariantCulture.Fixes(receiverType)
                ? InvariantCulture.ObjectText(Bound, receiverType)
                : $"{(target is null ? "" : target + ".")}{BodyEmitter.FunctionName(reference)}({string.Join(", ", callArguments)})";
        // The delegate retains Mist's synchronous result contract, so completion must precede its return.
        if(AsyncPromotion.IsPromoted(method))
        {
            call += ".GetAwaiter().GetResult()";
        }

        var lambda = $"({string.Join(", ", arguments)}) => {call}";

        return receiver is null || receiverType is null
            ? $"(({function})({lambda}))"
            : $"((System.Func<{TypeNameEmitter.Emit(receiverType)}, {function}>)({Bound} => {lambda}))({receiver})";
    }

    private static IReadOnlyList<string> PackArguments(FunctionReferenceExpression reference, IReadOnlyList<string> arguments)
    {
        var parameters = reference.Method.ParametersList;
        if(ValuePack.FlattenedSlot(parameters) is not { } slot || ValuePack.PackParameterOf(parameters[slot]) is null)
        {
            return arguments;
        }

        var count = arguments.Count - parameters.Count + 1;
        var elements = string.Join(", ", arguments.Skip(slot).Take(count));
        var tuple = count switch
        {
            0 => "default(System.ValueTuple)",
            1 => $"System.ValueTuple.Create({elements})",
            _ => $"({elements})",
        };
        return [.. arguments.Take(slot), tuple, .. arguments.Skip(slot + count)];
    }

    private static IMistType OpenOwner(MemberType owner)
    {
        var enclosing = owner.DeclaringType is MemberType parent ? OpenOwner(parent) as ConstructedType : null;
        return owner.TypeParametersList.Count == 0 && enclosing is null
            ? owner
            : new ConstructedType(owner, owner.TypeParametersList, enclosing);
    }
}