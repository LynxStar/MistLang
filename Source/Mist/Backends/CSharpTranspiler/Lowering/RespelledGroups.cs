using Mist.Backends.CSharpTranspiler.Emitter;
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
        return AsyncPromotion.IsPromoted(reference.Method)
            || DefaultWitnesses.StaticParameters(reference.Method).Count > 0
            || reference.Method.TypeParametersList.Any(DefaultConstraintWitness.Constrains)
            || ConstructorWitnesses.Of(reference.Method.TypeParametersList).Count > 0
            || (reference.Method is { Modifiers.IsStatic: true, DeclaringType: ConcreteType owner }
                && ConstructorWitnesses.Stored(owner).Count > 0)
            || IntrinsicCalls.Respells(reference.Method)
            || (InvariantCulture.FormatsItself(reference.Method) && reference.Receiver is { } receiver && !InvariantCulture.Fixes(receiver.Type));
    }

    public static string Lambda(FunctionReferenceExpression reference, string? receiver)
    {
        var method = reference.Method;
        var function = TypeNameEmitter.Emit(reference.Type);
        var arguments = method.ParametersList.Select((_, i) => $"__arg{i}").ToList();
        var receiverType = reference.Receiver?.Type;
        if(receiver is null && !method.Modifiers.IsStatic && method.DeclaringType is StructType owner)
        {
            receiver = ReceiverSpelling.This;
            receiverType = OpenOwner(owner);
        }

        var captured = receiver is null ? null : Bound;
        var callArguments = DefaultWitnesses.MethodArguments(method, reference.TypeArguments, receiverType ?? method.DeclaringType)
            .Select(argument => argument ?? "/* unsupported: default constraint */").Concat(arguments);
        var target = captured ?? (method.Modifiers.IsStatic
            ? method.DeclaringType is { } declaring
                ? TypeNameEmitter.Emit(receiverType is null ? declaring : UnsafeReach.ClosedOwner(declaring, receiverType))
                : null
            : ReceiverSpelling.This);
        var call = IntrinsicCalls.Respells(method)
            ? IntrinsicCalls.Rewrite(method, captured, receiverType, arguments)
            : InvariantCulture.FormatsItself(method) && receiverType is not null && !InvariantCulture.Fixes(receiverType)
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

    private static IMistType OpenOwner(MemberType owner)
    {
        var enclosing = owner.DeclaringType is MemberType parent ? OpenOwner(parent) as ConstructedType : null;
        return owner.TypeParametersList.Count == 0 && enclosing is null
            ? owner
            : new ConstructedType(owner, owner.TypeParametersList, enclosing);
    }
}