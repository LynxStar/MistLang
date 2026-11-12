using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A dependent parameter read through a value of a carrier-routed type, <c>ring.Capacity</c>. The carrier
/// keeps the value in the closing rather than in the object, so the read is the closing's static
/// projection, which is what the same name reads through the type; the receiver is evaluated for its
/// effects and nothing more. Array and the spans are not this: the CLR object holds their length.
/// </summary>
internal static class InstanceSlotReads
{
    public static (ConstructedType Closing, TypeParameter Slot)? Through(MemberAccessExpression access)
    {
        if(access.Receiver is TypeReferenceExpression || access.Member is not TypeResult found || InstanceSlotRead.Parameter(found) is not { } slot)
        {
            return null;
        }

        var unaliased = access.Receiver.Type is TypeAlias { AliasedType: { } aliased } ? aliased : access.Receiver.Type;
        var receiver = (unaliased is RefinedType refined ? refined.BaseType : unaliased) as ConstructedType;

        return InstanceSlotRead.Closing(found, receiver, interner: null) is { Definition: MemberType { Modifiers.IsIntrinsic: false } } closing
            && !LengthErasure.IsLengthIndexed(closing)
                ? (closing, slot)
                : null;
    }

    public static bool IsPlainRead(Expression receiver)
    {
        return receiver is LocalReferenceExpression or FieldReferenceExpression or ThisExpression;
    }
}