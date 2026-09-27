using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a member reads only, or writes storage. Beside <see cref="ThisTiers"/> and for the same
/// reason: both answer what a BODY does, and one walk with two questions beats two walkers that can
/// disagree. The rules are the readonly-contexts spec's base rules (Req 11) read as a classification --
/// a field written, a setter called, a callee that is not readonly, an allocation through a constructor
/// that is not -- with a constructor privileged to assign the fields of its own type (Req 10.7), and
/// with the purity spec's amendment (its Req 11): a write to what the context owns by
/// <see cref="Origins"/>, and a call to a local member on such a receiver, are not writes of the
/// context. Which members are local is the caller's oracle, because the answer is the purity fixpoint's
/// and that fixpoint asks this class in turn; without one, only a declared <c>local</c> counts.
/// <para>
/// A call asks the CALLEE'S status and never its body (Req 10.8), which is what makes this a fixed
/// point over the call graph rather than an inlining. It is the GREATEST one: a member is readonly
/// unless something reachable from it writes, so two methods that only call each other settle readonly
/// rather than deadlocking on each other's answer. A member with no Mist body -- intrinsic, CLR-projected
/// or abstract -- is readonly only if it SAYS so, since there is no body here to prove it, and an
/// auto-getter is readonly by construction because reading a field is all it can do (Req 10.4).
/// </para>
/// <para>
/// The walk memoises only what it PROVED: everything reached when nothing writes, and the root alone
/// when something does -- a member reached on a failing path may still be readonly by itself, and
/// settling it from that path would answer a question the walk never asked.
/// </para>
/// <para>
/// A write THROUGH AN INDEX counts when the receiver chain roots at `this` or a field, which is what
/// makes `cells[0] = v` a write of the value's own storage and a user indexer's setter on a field one
/// too. It does not count when the chain roots at a local or a parameter: a readonly member filling a
/// scratch array it just allocated mutates nothing the value reaches, and refusing that would refuse
/// working code.
/// </para>
/// </summary>
internal sealed class ReadonlyInference(Func<object, bool>? localMembers = null)
{
    private readonly Dictionary<object, bool> settled = new(ReferenceEqualityComparer.Instance);

    public bool IsReadonly(Method method)
    {
        return method.Modifiers.IsReadonly || (method.Body is not null && Settle(method));
    }

    public bool IsReadonlyGetter(Property property)
    {
        return property.Modifiers.IsReadonly || IsReadonlyAccessor(property.Getter);
    }

    public bool IsReadonlyGetter(Indexer indexer)
    {
        return indexer.Modifiers.IsReadonly || IsReadonlyAccessor(indexer.Getter);
    }

    public bool IsReadonlySetter(PropertyAccessor setter)
    {
        return !setter.IsAuto && Settle(setter);
    }

    public bool IsReadonly(LambdaExpression lambda)
    {
        return Settle(lambda);
    }

    private bool IsLocalMember(object member, MemberModifiers declared)
    {
        return localMembers?.Invoke(member) ?? declared.IsLocal;
    }

    private bool IsReadonlyAccessor(PropertyAccessor? accessor)
    {
        return accessor is not null && (accessor.IsAuto || Settle(accessor));
    }

    private bool Settle(object root)
    {
        if(settled.TryGetValue(root, out var known))
        {
            return known;
        }

        var reached = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);

        while(pending.Count > 0)
        {
            var member = pending.Pop();

            if(!seen.Add(member))
            {
                continue;
            }

            if(settled.TryGetValue(member, out var before))
            {
                if(before)
                {
                    continue;
                }

                settled[root] = false;
                return false;
            }

            var callees = new List<object>();

            if(Writes(member, callees))
            {
                settled[root] = false;
                return false;
            }

            reached.Add(member);

            foreach(var callee in callees)
            {
                pending.Push(callee);
            }
        }

        foreach(var member in reached)
        {
            settled[member] = true;
        }

        return true;
    }

    private bool Writes(object member, List<object> callees)
    {
        foreach(var root in Roots(member))
        {
            var origins = new Origins(root, member, ScopeKind.Readonly);

            foreach(var node in BodyWalker.DescendantsAndSelf(root))
            {
                if(Writes(node, origins, member, callees))
                {
                    return true;
                }
            }

            foreach(var exit in PurityJudge.ExitDrops(root, member))
            {
                if(!IsLocalMember(exit.Drop, exit.Drop.Modifiers) && Unproven(exit.Drop, callees))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // An intrinsic operator is the target's own arithmetic, with no body that could write.
    private bool Writes(object node, Origins origins, object member, List<object> callees)
    {
        return node switch
        {
            AssignmentExpression assignment => Writes(assignment.Target, origins, member) || StoresUnowned(origins, assignment),
            ObjectInitializerAssignment entry => StoresUnowned(origins, entry),
            IndexExpression { Indexer: { } indexer } indexed =>
                !OwnLocalCallee(indexer.Getter, indexer.Modifiers, origins.Of(indexed.Receiver))
                && UnprovenGetter(indexer.Modifiers.IsReadonly, indexer.Getter, callees),
            BinaryExpression { Operator: { Modifiers.IsIntrinsic: false } binaryOperator } => Unproven(binaryOperator, callees),
            UnaryExpression { Operator: { Modifiers.IsIntrinsic: false } unaryOperator } => Unproven(unaryOperator, callees),
            InvocationExpression invocation => !OwnLocalCallee(invocation, origins) && Unproven(invocation.Method, callees),
            NewExpression { Constructor: { } constructor } => Unproven(constructor, callees),
            PropertyReferenceExpression { Property: { } read } => UnprovenGetter(read.Modifiers.IsReadonly, read.Getter, callees),
            MemberAccessExpression { Member: PropertyResult { Property: { } accessed } } access =>
                !OwnLocalCallee(accessed.Getter, accessed.Modifiers, HolderOf(origins, access))
                && UnprovenGetter(accessed.Modifiers.IsReadonly, accessed.Getter, callees),
            ForEachStatement loop => LoopWrites(loop, origins, callees),
            _ => false,
        };
    }

    private bool Writes(Expression target, Origins origins, object member)
    {
        return target switch
        {
            _ when Target(target) is { } written => !Privileged(member, written) && HolderOf(origins, target) is not Origin.Own,
            IndexExpression { Indexer: { } indexer } => HolderOf(origins, target) is not Origin.Own || !LocalSetter(indexer.Setter, indexer.Modifiers),
            IndexExpression => HolderOf(origins, target) is not Origin.Own && IndexedWriteTargetName(target) is not null,
            BackingFieldReferenceExpression or PropertyReferenceExpression => true,
            MemberAccessExpression { Member: PropertyResult { Property: var set } } => HolderOf(origins, target) is not Origin.Own || !LocalSetter(set.Setter, set.Modifiers),
            _ => false,
        };
    }

    private bool LoopWrites(ForEachStatement loop, Origins origins, List<object> callees)
    {
        var holder = origins.Of(loop.Collection);

        foreach(var method in new[] { loop.GetEnumerator, loop.MoveNext })
        {
            if(method is not null && !(holder is Origin.Own && IsLocalMember(method, method.Modifiers)) && Unproven(method, callees))
            {
                return true;
            }
        }

        return loop.Current is { } current
            && !OwnLocalCallee(current.Getter, current.Modifiers, holder)
            && UnprovenGetter(current.Modifiers.IsReadonly, current.Getter, callees);
    }

    /// <summary>
    /// A store that would make the context's next read through an OWNED slot answer Own for an object
    /// other references share: an external or frozen reference of a type that can change, into an owned
    /// field or property of something the context owns. The write itself is admitted by the amendment;
    /// this is the one write of what a context owns that it may not make.
    /// </summary>
    public static bool StoresUnowned(Origins origins, AssignmentExpression assignment)
    {
        var owned = assignment.Target switch
        {
            FieldReferenceExpression field => ThisTiers.IsOwned(field.Field),
            MemberAccessExpression { Member: FieldResult found } => ThisTiers.IsOwned(found.Field),
            PropertyReferenceExpression property => property.Property.Modifiers is { IsOwned: true, IsStatic: false },
            MemberAccessExpression { Member: PropertyResult found } => found.Property.Modifiers is { IsOwned: true, IsStatic: false },
            _ => false,
        };

        return owned && HolderOf(origins, assignment.Target) is Origin.Own && Unowned(origins, assignment.Value);
    }

    public static bool StoresUnowned(Origins origins, ObjectInitializerAssignment entry)
    {
        var owned = entry.Member switch
        {
            FieldResult found => ThisTiers.IsOwned(found.Field),
            PropertyResult found => found.Property.Modifiers is { IsOwned: true, IsStatic: false },
            _ => false,
        };

        return owned && Unowned(origins, entry.Value);
    }

    private static bool Unowned(Origins origins, Expression value)
    {
        return !Origins.IsUnchangeable(value.Type) && origins.Of(value) is not Origin.Own;
    }

    private bool OwnLocalCallee(InvocationExpression invocation, Origins origins)
    {
        return invocation.Method is { Modifiers: { IsStatic: false } modifiers } callee
            && ThisTiers.ReceiverOf(invocation) is { } receiver
            && origins.Of(receiver) is Origin.Own
            && IsLocalMember(callee, modifiers);
    }

    private bool OwnLocalCallee(PropertyAccessor? getter, MemberModifiers owner, Origin holder)
    {
        return getter is not null && !owner.IsStatic && holder is Origin.Own && IsLocalMember(getter, owner);
    }

    private bool LocalSetter(PropertyAccessor? setter, MemberModifiers owner)
    {
        return setter is not null && IsLocalMember(setter, owner);
    }

    public static Origin HolderOf(Origins origins, Expression target)
    {
        return target switch
        {
            MemberAccessExpression { Member: FieldResult { Field.Modifiers.IsStatic: true } or PropertyResult { Property.Modifiers.IsStatic: true } } => Origin.External,
            MemberAccessExpression access => origins.Of(access.Receiver),
            IndexExpression index => origins.Of(index.Receiver),
            _ => origins.This,
        };
    }

    private static bool Privileged(object member, Field written)
    {
        return member is Constructor constructor
            && constructor.DeclaringType is { } built
            && ReferenceEquals(written.DeclaringType, built);
    }

    private static bool Unproven(Method? callee, List<object> callees)
    {
        if(callee is null)
        {
            return true;
        }

        if(callee.Modifiers.IsReadonly)
        {
            return false;
        }

        if(callee.Body is null)
        {
            return true;
        }

        callees.Add(callee);
        return false;
    }

    private static bool UnprovenGetter(bool declaredReadonly, PropertyAccessor? getter, List<object> callees)
    {
        if(declaredReadonly || getter is { IsAuto: true })
        {
            return false;
        }

        if(getter is null || (getter.Body is null && getter.ExpressionBody is null))
        {
            return true;
        }

        callees.Add(getter);
        return false;
    }

    private static IEnumerable<object> Roots(object member)
    {
        switch(member)
        {
            case Method { Body: { } body }:
                yield return body;

                if(member is Constructor constructor)
                {
                    foreach(var initializer in PurityInference.InstanceInitializers(constructor))
                    {
                        yield return initializer;
                    }
                }

                break;

            case PropertyAccessor accessor:
                if(accessor.Body is { } accessorBody)
                {
                    yield return accessorBody;
                }

                if(accessor.ExpressionBody is { } expression)
                {
                    yield return expression;
                }

                break;

            case LambdaExpression lambda:
                if(((object?)lambda.Body ?? lambda.Result) is { } lambdaBody)
                {
                    yield return lambdaBody;
                }

                break;
        }
    }

    public static string? IndexedWriteTargetName(Expression target)
    {
        return target is IndexExpression index ? Rooted(index.Receiver) : null;
    }

    private static string? Rooted(Expression receiver)
    {
        return receiver switch
        {
            FieldReferenceExpression field => field.Field.Name,
            MemberAccessExpression { Member: FieldResult found } => found.Field.Name,
            MemberAccessExpression access => Rooted(access.Receiver),
            IndexExpression nested => Rooted(nested.Receiver),
            ThisExpression => "this",
            _ => null,
        };
    }

    private static Field? Target(Expression target)
    {
        return target switch
        {
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { Member: FieldResult found } => found.Field,
            _ => null,
        };
    }
}