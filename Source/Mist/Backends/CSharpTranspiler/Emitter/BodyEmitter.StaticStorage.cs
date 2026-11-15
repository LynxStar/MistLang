using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A read or a write of static storage that <see cref="StaticHolders"/> keeps per value: the holder the closing's
/// values find, then the member. A closing written in the program supplies its values as a call through it does, a
/// capture's initializer evaluated here and nowhere else; a bare name inside the type's own members, or a type
/// deriving from it, supplies the hidden field or the hidden parameters through the closing the enclosing type
/// names, which <see cref="SlotReach"/> refuses where nothing carries them. A struct holds copies of its
/// ancestors' statics, so a struct's own holder keeps them. The holder being built reaches its own members through
/// itself, which is also the one place a readonly static is assigned. A hidden field or parameter is a C# local a
/// static lambda or a static local function may not capture, so either written static keeps the modifier only where
/// it reads none.
/// </summary>
internal static partial class BodyEmitter
{
    private static string? HeldStorage(LookupResult? member, Expression? receiver, DiagnosticBag diagnostics)
    {
        return member switch
        {
            FieldResult { Field: var field } => HeldStorage(field, receiver, diagnostics),
            PropertyResult { Property: var property } => HeldStorage(property, receiver, diagnostics),
            _ => null,
        };
    }

    private static string? HeldStorage(Field field, Expression? receiver, DiagnosticBag diagnostics)
    {
        return HeldOwner(field, receiver) is { } owner ? $"{Holder(owner, receiver, diagnostics)}.{IdentifierSpelling.Of(field.Name)}" : null;
    }

    private static string? HeldStorage(Property property, Expression? receiver, DiagnosticBag diagnostics)
    {
        return HeldOwner(property, receiver) is { } owner ? $"{Holder(owner, receiver, diagnostics)}.{IdentifierSpelling.Of(property.Name)}" : null;
    }

    private static MemberType? HeldOwner(MemberAccessExpression access)
    {
        return access.Member switch
        {
            FieldResult { Field: var field } => HeldOwner(field, access.Receiver),
            PropertyResult { Property: var property } => HeldOwner(property, access.Receiver),
            _ => null,
        };
    }

    private static MemberType? HeldOwner(Field field, Expression? receiver)
    {
        return (field.DeclaringType ?? field.StorageSource?.DeclaringType) is { } declaring && StorageOwner(declaring, receiver) is var owner && StaticHolders.IsHeld(field, owner)
            ? owner
            : null;
    }

    private static MemberType? HeldOwner(Property property, Expression? receiver)
    {
        return property.DeclaringType is { } declaring && StorageOwner(declaring, receiver) is var owner && StaticHolders.IsHeld(property, owner) ? owner : null;
    }

    private static MemberType StorageOwner(MemberType declaring, Expression? receiver)
    {
        var through = receiver is TypeReferenceExpression reference
            ? Unaliased(TypeNameEmitter.Closed(reference.ReferencedType)) switch
            {
                ConstructedType constructed => constructed.Definition,
                var written => written,
            }
            : FieldReceiver.Emitting;

        return through is StructType copier && !ReferenceEquals(copier, declaring) && UnsafeReach.DerivesFrom(copier, declaring) ? copier : declaring;
    }

    private static string? HeldReceiver(Expression target, Expression part, DiagnosticBag diagnostics)
    {
        return target is MemberAccessExpression access && ReferenceEquals(access.Receiver, part) && HeldOwner(access) is { } owner
            ? Holder(owner, part, diagnostics)
            : null;
    }

    private static string Holder(MemberType owner, Expression? receiver, DiagnosticBag diagnostics)
    {
        if(receiver is not null && boundParts?.GetValueOrDefault(receiver) is { } bound)
        {
            return bound;
        }

        var closing = receiver is TypeReferenceExpression reference
            ? WrittenClosing(owner, reference) as ConstructedType
            : SlotRoutes.ClosingFrom(enclosingType ?? FieldReceiver.Emitting, owner);

        if(StaticHolders.IsBuilding(owner) && (closing is null || IsOwnClosing(closing, owner)))
        {
            return "this";
        }

        var values = closing is null ? HiddenReads(owner) : SlotValues(closing, diagnostics);
        var factories = StaticHolders.Factories(owner);
        values = factories.Count == 0 ? values
            : values.Concat(closing is null ? ConstructorWitnesses.Names(factories) : ConstructorWitnesses.Arguments(factories, closing));

        return StaticHolders.Through(TypeNameEmitter.Emit(closing ?? new ConstructedType(owner, owner.TypeParametersList)), values);
    }

    private static IMistType WrittenClosing(MemberType owner, TypeReferenceExpression reference)
    {
        var written = Unaliased(TypeNameEmitter.Closed(reference.ReferencedType));

        return written is ConstructedType { Enclosing: not null } nested && ReferenceEquals(nested.Definition, owner)
            ? nested
            : UnsafeReach.ClosedOwner(owner, written);
    }

    private static bool IsOwnClosing(ConstructedType closing, MemberType owner)
    {
        return SlotRoutes.Carried(owner).All(slot => ReferenceEquals(SlotRoutes.Argument(closing, slot), slot));
    }

    private static bool ConsumedByHolder(MemberAccessExpression access, SlotCapture capture)
    {
        return access.Receiver is TypeReferenceExpression reference
            && HeldOwner(access) is { } owner
            && WrittenClosing(owner, reference) is var closed
            && (ConsumedByClosing(closed, capture) || ConsumedByWitness(closed, capture));
    }

    internal static bool ReadsHiddenValues(object body)
    {
        return SlotRoutes.Carried(enclosingType).Count > 0
            || BodyWalker.DescendantsAndSelf(body).Any(node => node switch
            {
                TypeReferenceExpression reference => Mentions(reference.ReferencedType),
                InvocationExpression invocation when CallTypeArguments(invocation) is var (arguments, _, _) && arguments.Any(Mentions) => true,
                Expression expression => Mentions(expression.Type),
                TypeTestPattern typeTest => Mentions(typeTest.NarrowedType),
                PropertyPattern property => Mentions(property.NarrowedType),
                _ => false,
            });
    }

    private static bool Mentions(IMistType type)
    {
        return TypeParameterReach.Collect(type).Any(parameter => SlotRoutes.IsValueRouted(parameter) || ClosingWitnesses.IsWitnessed(parameter));
    }
}