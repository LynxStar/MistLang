using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The closing a member reached through a value is substituted with (runtime-dependent-values Requirement
/// 7.6). A slot the receiver's type closes with a capture or a refinement stands for what is provable about
/// the receiver's value, not for the value itself, so the member's signature reads the receiver's own slot
/// instead: a capture whose initializer is that read, owned by the node that reaches the member, which an
/// argument or a write meets by equality, for a method, a field, a property or an indexer. Only a receiver that
/// is a plain read is given one, since the capture reads the receiver again; through any other receiver the
/// member keeps the type's closing, and Stage 5 meets those slots through the receiver as evaluated.
/// </summary>
internal static class ReceiverSlots
{
    public static (ConstructedType? Closing, IReadOnlyList<SlotCapture> Captures) Own(Expression receiver, ConstructedType? closing, TypeInterner interner)
    {
        if(closing is not { Definition: ClassType or StructType } || closing.Definition is not IHasTypeParameters declared || !Readable(receiver))
        {
            return (closing, []);
        }

        var arguments = closing.TypeArguments.ToArray();
        var captures = new List<SlotCapture>();

        foreach(var parameter in declared.TypeParametersList.Where(parameter => parameter is { IsDependent: true, IsPack: false } && parameter.Ordinal < arguments.Length))
        {
            if(arguments[parameter.Ordinal] is not (CapturedValue or RefinementArgument) || InstanceSlotRead.Contract(parameter) is not { } contract)
            {
                continue;
            }

            var read = new MemberAccessExpression(receiver.Node, receiver, parameter.Name, contract, member: new TypeResult(parameter));

            if(SlotValueBinder.Capture(read, parameter, $"{SlotValueBinder.Written(receiver.Node, "receiver")}.{parameter.Name}", interner, converted: true) is { } capture)
            {
                arguments[parameter.Ordinal] = capture.Value;
                captures.Add(capture);
            }
        }

        return captures.Count == 0 ? (closing, []) : (interner.GetOrCreateConstructedType(closing.Definition, arguments, closing.Enclosing), captures);
    }

    public static IReadOnlyList<SlotCapture> Used(IReadOnlyList<SlotCapture> captures, IEnumerable<IMistType> types)
    {
        if(captures.Count == 0)
        {
            return captures;
        }

        var mentioned = types.ToList();

        return [.. captures.Where(capture => mentioned.Exists(type => CaptureScope.Mentions(type, capture)))];
    }

    private static bool Readable(Expression receiver)
    {
        return receiver switch
        {
            LocalReferenceExpression or FieldReferenceExpression or PropertyReferenceExpression => true,
            MemberAccessExpression { Member: FieldResult, Receiver: var inner } => inner is ThisExpression || Readable(inner),
            _ => false,
        };
    }
}