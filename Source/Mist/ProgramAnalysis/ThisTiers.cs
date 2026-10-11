using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// The access to `this` a method needs and a receiver gives, on the one lattice: unrestricted,
/// readonly ref, frozen, borrowed, owned. A method's need is what it declares with `with`, or else
/// what its body does -- assigning an owned field needs borrowed, and a call through an explicit
/// `this.` needs whatever the callee needs. An implicit call is NOT followed and a bare field's
/// call is judged as unrestricted where `this.field` is not, because that is exactly the line
/// classic draws, measured; receiver/lending inference precision remains MIST-153 in backlog/issues.yaml.
/// </summary>
internal sealed class ThisTiers
{
    private readonly Dictionary<Method, ThisTier> inferred = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Method> visiting = new(ReferenceEqualityComparer.Instance);

    public static string Display(ThisTier tier)
    {
        return tier switch
        {
            ThisTier.None => "unrestricted",
            ThisTier.ReadonlyRef => "readonly ref",
            ThisTier.Frozen => "frozen",
            ThisTier.Borrowed => "borrowed",
            _ => "owned",
        };
    }

    public ThisTier Required(Method method)
    {
        return method.DeclaredThisTier ?? Inferred(method);
    }

    public ThisTier Inferred(Method method)
    {
        if(inferred.TryGetValue(method, out var known))
        {
            return known;
        }

        if(method.Body is null || !visiting.Add(method))
        {
            return ThisTier.None;
        }

        var needed = ThisTier.None;

        foreach(var node in BodyWalker.DescendantsAndSelf(method.Body))
        {
            var demand = node switch
            {
                AssignmentExpression assignment when Deconstructions.Pairs(assignment).Any(pair => WritesOwnedField(pair.Target)) => ThisTier.Borrowed,
                InvocationExpression { Receiver: MemberAccessExpression { Receiver: ThisExpression }, Method: { } callee } => Required(callee),
                _ => ThisTier.None,
            };

            if(demand > needed)
            {
                needed = demand;
            }
        }

        _ = visiting.Remove(method);
        inferred[method] = needed;
        return needed;
    }

    private static bool WritesOwnedField(Expression target)
    {
        return (target is FieldReferenceExpression { Field: { } written } && IsOwned(written))
            || (target is MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult { Field: { } through } } && IsOwned(through));
    }

    public ThisTier Provided(Expression? receiver, object? enclosing)
    {
        return receiver switch
        {
            null => ThisTier.Owned,
            ThisExpression => enclosing is Method { } method and not Constructor ? Required(method) : ThisTier.Owned,
            LocalReferenceExpression local => Declared(local.Local),
            FieldReferenceExpression field => IsOwned(field.Field) ? ThisTier.Owned : ThisTier.None,
            _ => ThisTier.Owned,
        };
    }

    public static ThisTier Declared(LocalVariable local)
    {
        return local switch
        {
            { Ownership: OwnershipKind.Owned } => ThisTier.Owned,
            { Ownership: OwnershipKind.Borrowed } => ThisTier.Borrowed,
            { Ownership: OwnershipKind.Frozen } => ThisTier.Frozen,
            { IsRef: true, Parameter.Modifier.IsReadonly: true } or { IsRef: true, IsReadonly: true } => ThisTier.ReadonlyRef,
            { Parameter: not null } => ThisTier.None,
            _ => ThisTier.Owned,
        };
    }

    public static bool IsOwned(Field field)
    {
        return !field.Modifiers.IsStatic
            && (field.Ownership == OwnershipKind.Owned || field.Modifiers.IsOwned || field.DeclaringType is { Modifiers.IsOwned: true });
    }

    public static string? OwnedSlot(Expression target)
    {
        return target switch
        {
            FieldReferenceExpression { Field: var field } when IsOwned(field) => $"owned field '{field.Name}'",
            MemberAccessExpression { Member: FieldResult { Field: var field } } when IsOwned(field) => $"owned field '{field.Name}'",
            MemberAccessExpression { Member: PropertyResult { Property: { Modifiers: { IsOwned: true, IsStatic: false } } property } } => $"owned property '{property.Name}'",
            PropertyReferenceExpression { Property: { Modifiers: { IsOwned: true, IsStatic: false } } property } => $"owned property '{property.Name}'",
            _ => null,
        };
    }

    public static Expression? ReceiverOf(InvocationExpression invocation)
    {
        return invocation.Receiver switch
        {
            MemberAccessExpression access => access.Receiver,
            FunctionReferenceExpression function => function.Receiver,
            MethodGroupExpression group => group.Receiver,
            _ => null,
        };
    }

    public static bool IsValue(IMistType type)
    {
        return type is StructType or ConstructedType { Definition: StructType };
    }
}