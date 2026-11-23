using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The slots of a member's signature that are the receiver's own where the receiver's class or struct
/// closes them with <c>unknown</c> (runtime-dependent-values Requirement 7.6). An object is one closing
/// of its own class, so such a slot still holds one value, the receiver's own read. The binder leaves the
/// slot <c>unknown</c>, and one <c>unknown</c> stands for every unknown value of a domain, so which slot is
/// the receiver's is read from where the declared signature names the receiver's own parameter, or the
/// base's parameter its class closes with that one, or the extension block's parameter its receiver type
/// closes with that one, never from the closed type argument. Each becomes a
/// capture initialized by that read, which <see cref="SlotMeeting"/> meets by equality, as it meets a slot
/// the binder gave the receiver's read.
/// </summary>
internal static class OwnSlots
{
    public static IMistType Mark(Expression? receiver, IMistType? declared, IMistType closed, IDictionary<SlotCapture, TypeParameter> owned)
    {
        return declared is not null && receiver is not (null or TypeReferenceExpression or ThisExpression)
            && SlotReads.Receiving(receiver.Type) is ConstructedType { Definition: ClassType or StructType } closing
                ? Marked(receiver, declared, closed, closing, owned)
                : closed;
    }

    public static (Expression Receiver, IMistType Declared)? Reached(Expression value)
    {
        return SlotReads.Written(value) switch
        {
            MemberAccessExpression { Member: FieldResult field, Receiver: { } receiver } => (receiver, field.Field.Type),
            MemberAccessExpression { Member: PropertyResult property, Receiver: { } receiver } => (receiver, property.Property.Type),
            InvocationExpression { Receiver: MemberAccessExpression { Receiver: { } receiver }, Signature: { } method } => (receiver, method.ReturnType),
            IndexExpression { Indexer: { } indexer, Receiver: var receiver } => (receiver, indexer.Type),
            _ => null,
        };
    }

    public static (Expression Receiver, TypeParameter Own)? Slot(Expression value, int ordinal)
    {
        return Reached(value) is var (receiver, declared) && receiver is not (TypeReferenceExpression or ThisExpression)
            && SlotReads.Receiving(receiver.Type) is ConstructedType { Definition: ClassType or StructType } closing
            && SlotReads.Plain(declared) is ConstructedType open && ordinal < open.TypeArguments.Count
            && open.TypeArguments[ordinal] is TypeParameter { IsDependent: true, IsPack: false } named && Own(named, closing) is { } own
                ? (receiver, own)
                : null;
    }

    private static IMistType Marked(Expression receiver, IMistType declared, IMistType closed, ConstructedType closing, IDictionary<SlotCapture, TypeParameter> owned)
    {
        if(declared is TypeParameter { IsDependent: true, IsPack: false } named && Unknown(closed)
            && Own(named, closing) is { Domain: { } domain } parameter
            && parameter.Ordinal < closing.TypeArguments.Count
            && Unknown(closing.TypeArguments[parameter.Ordinal]))
        {
            var read = SlotReads.Read(receiver, parameter);
            var capture = new SlotCapture(
                $"{RefinementFindings.Display(receiver)}.{parameter.Name}", InstanceSlotRead.Contract(parameter) ?? domain, ConversionRelation.Unrefined(domain), read,
                receiver.Node.Span.Start);
            owned[capture] = parameter;
            return capture.Value;
        }

        if(closed is not ConstructedType shut || SlotReads.Plain(declared) is not ConstructedType open
            || !ReferenceEquals(open.Definition, shut.Definition) || open.TypeArguments.Count != shut.TypeArguments.Count)
        {
            return closed;
        }

        var arguments = shut.TypeArguments.Select((argument, i) => Marked(receiver, open.TypeArguments[i], argument, closing, owned)).ToList();

        return arguments.Where((argument, i) => !ReferenceEquals(argument, shut.TypeArguments[i])).Any()
            ? new ConstructedType(shut.Definition, arguments, shut.Enclosing)
            : closed;
    }

    private static TypeParameter? Own(TypeParameter parameter, ConstructedType closing)
    {
        if(closing.Definition is not IHasTypeParameters declaring)
        {
            return null;
        }

        parameter = Received(parameter) ?? parameter;
        var mapped = ReferenceEquals(parameter.DeclaringType, closing.Definition) ? parameter
            : parameter.DeclaringType is { } definition
                && SlotReads.Closing(new ConstructedType(closing.Definition, declaring.TypeParametersList), definition) is { } path
                && parameter.Ordinal < path.TypeArguments.Count
                ? path.TypeArguments[parameter.Ordinal] as TypeParameter
                : null;

        return mapped is { IsDependent: true, IsPack: false } && declaring.TypeParametersList.Exists(own => ReferenceEquals(own, mapped)) ? mapped : null;
    }

    private static TypeParameter? Received(TypeParameter parameter)
    {
        if(parameter.Scope is not ExtensionScope { Block.ResolvedReceiverType: { } received }
            || SlotReads.Plain(received) is not ConstructedType { Definition: IHasTypeParameters host } receiving)
        {
            return null;
        }

        for(var i = 0; i < receiving.TypeArguments.Count && i < host.TypeParametersList.Count; i++)
        {
            if(ReferenceEquals(receiving.TypeArguments[i], parameter))
            {
                return host.TypeParametersList[i];
            }
        }

        return null;
    }

    private static bool Unknown(IMistType argument)
    {
        return argument is UnknownType or (UnknownValueType and not RefinementArgument);
    }
}