using Mist.ProgramModel.Construction.Binding;
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
/// what its body does -- assigning an owned field needs borrowed, lending `this` to a parameter needs
/// that parameter's tier, and a call through an explicit `this.` needs whatever the callee needs. An
/// implicit call is NOT followed and a bare field's call is judged as unrestricted where `this.field`
/// is not, because that is exactly the line classic draws, measured; receiver/lending inference
/// precision remains MIST-153 in backlog/issues.yaml. Which fields of `this` a method touches, for a
/// call on a holder whose owned fields are partly moved or lent (owned-fields Requirement 16.23), does
/// follow implicit calls too: a sibling the callee never reaches is the question, and a helper reached
/// without `this.` reaches fields all the same.
/// </summary>
internal sealed class ThisTiers
{
    private readonly Dictionary<Method, ThisTier> inferred = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Method> visiting = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Method, IReadOnlyDictionary<Field, FieldUse>> uses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Method> visitingUses = new(ReferenceEqualityComparer.Instance);

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
                InvocationExpression { Receiver: MemberAccessExpression { Receiver: ThisExpression }, Method: { } callee } invocation
                    => Max(Required(callee), Lent(invocation.Arguments, invocation.Parameters)),
                InvocationExpression invocation => Lent(invocation.Arguments, invocation.Parameters),
                NewExpression creation => Lent(creation.Arguments, creation.Constructor?.ParametersList),
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

    /// <summary>
    /// The instance fields of the declaring type a method reads or writes, its own body and every
    /// instance method it calls on `this` included; empty for a method whose body is not in source.
    /// </summary>
    public IReadOnlyDictionary<Field, FieldUse> FieldUses(Method method)
    {
        if(uses.TryGetValue(method, out var known))
        {
            return known;
        }

        var found = new Dictionary<Field, FieldUse>(ReferenceEqualityComparer.Instance);

        if(method.Body is null || !visitingUses.Add(method))
        {
            return found;
        }

        var written = new HashSet<Expression>(ReferenceEqualityComparer.Instance);

        foreach(var assignment in BodyWalker.DescendantsAndSelf(method.Body).OfType<AssignmentExpression>())
        {
            foreach(var (target, _) in Deconstructions.Pairs(assignment))
            {
                _ = written.Add(target);
            }
        }

        foreach(var node in BodyWalker.DescendantsAndSelf(method.Body))
        {
            switch(node)
            {
                case FieldReferenceExpression { Field: { Modifiers.IsStatic: false } field } reference:
                    Record(found, field, written.Contains(reference));
                    break;

                case MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult { Field: { Modifiers.IsStatic: false } field } } access:
                    Record(found, field, written.Contains(access));
                    break;

                case InvocationExpression { Method: { Modifiers.IsStatic: false } callee } invocation
                    when ReceiverOf(invocation) is null or ThisExpression && ReferenceEquals(callee.DeclaringType, method.DeclaringType):
                    foreach(var (field, use) in FieldUses(callee))
                    {
                        found[field] = found.TryGetValue(field, out var reached) ? reached | use : use;
                    }

                    break;
            }
        }

        _ = visitingUses.Remove(method);
        uses[method] = found;
        return found;
    }

    private static void Record(Dictionary<Field, FieldUse> found, Field field, bool writes)
    {
        var use = writes ? FieldUse.Writes : FieldUse.Reads;
        found[field] = found.TryGetValue(field, out var known) ? known | use : use;
    }

    private static ThisTier Lent(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        var lent = ThisTier.None;

        for(var i = 0; parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(arguments[i] is ThisExpression self && !IsValue(self.Type))
            {
                lent = Max(lent, LendTier(parameters[i]));
            }
        }

        return lent;
    }

    public static ThisTier LendTier(Parameter parameter)
    {
        return parameter.Modifier switch
        {
            { Ownership: OwnershipKind.Borrowed } => ThisTier.Borrowed,
            { Ownership: OwnershipKind.Frozen } => ThisTier.Frozen,
            _ => ThisTier.None,
        };
    }

    private static ThisTier Max(ThisTier left, ThisTier right)
    {
        return left > right ? left : right;
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
        return OwnedProvenance.IsOwned(field);
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

[Flags]
internal enum FieldUse
{
    Reads = 1,
    Writes = 2,
}