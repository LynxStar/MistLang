using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a member reads only, or writes storage. Beside <see cref="ThisTiers"/> and for the same
/// reason: both answer what a BODY does, and one walk with two questions beats two walkers that can
/// disagree. The rules are the readonly-contexts spec's base rules (Req 11) read as a classification --
/// a field written, a setter called, a callee that is not readonly, an allocation through a constructor
/// that is not -- with a constructor privileged to assign the fields of the instance it builds
/// (Req 10.7), and with the purity spec's amendment (its Req 11): a write to what the context owns by
/// <see cref="Origins"/>, and a call to a local member on such a receiver, are not writes of the
/// context. The one write of what it owns that a context may not make is an external or frozen
/// reference of a type that can change stored into an owned field or property: the context's next read
/// through that owned slot would answer Own for an object other references share. What a callee
/// without a body here keeps -- an abstract slot, whose promise is its implementers', or a local
/// member -- is the caller's oracle, because the answer is the purity fixpoint's and that fixpoint asks
/// this class in turn; without one, only a declaration counts, and an auto accessor, which does nothing
/// but touch its own storage, counts as local.
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
/// Every write the context makes through anything External counts (purity Req 4.1): an element of an
/// array parameter, a ref parameter, a ref local aliasing a field, an initializer entry's setter. A
/// write through an index is admitted only into an array the context owns, so a readonly member filling
/// a scratch array it just allocated mutates nothing the value reaches, while one filling an array it
/// was handed writes the caller's storage.
/// </para>
/// </summary>
internal sealed partial class ReadonlyInference(Func<object, Promise>? inferred = null)
{
    private readonly Dictionary<object, bool> settled = new(ReferenceEqualityComparer.Instance);

    public bool IsReadonly(Method method)
    {
        return method.Modifiers.IsReadonly || (method.Body is not null && Settle(method));
    }

    public bool IsReadonly(ConcreteType type)
    {
        return type.Modifiers.IsReadonly || Settle(type);
    }

    public bool IsReadonly(TypeInitializer initializer)
    {
        return Settle(initializer);
    }

    public bool IsReadonlyGetter(Property property)
    {
        return IsReadonlyAccessor(property.Getter, property.Modifiers, property.DeclaringType);
    }

    public bool IsReadonlyGetter(Indexer indexer)
    {
        return IsReadonlyAccessor(indexer.Getter, indexer.Modifiers, indexer.DeclaringType);
    }

    public bool IsReadonlySetter(PropertyAccessor setter)
    {
        return !setter.IsAuto && Settle(setter);
    }

    public bool IsReadonly(LambdaExpression lambda)
    {
        return Settle(lambda);
    }

    public static MemberModifiers Declared(PropertyAccessor? accessor, MemberModifiers owner)
    {
        return accessor?.OwnModifiers ?? owner;
    }

    private bool IsLocalMember(object member, MemberModifiers declared)
    {
        return inferred is { } oracle ? oracle(member).Local : declared.IsLocal || member is PropertyAccessor { IsAuto: true };
    }

    private bool BodilessReadonly(object member, MemberModifiers declared)
    {
        return declared.IsReadonly || inferred?.Invoke(member).Readonly == true;
    }

    private bool IsReadonlyAccessor(PropertyAccessor? accessor, MemberModifiers owner, MemberType? declaringType)
    {
        if(accessor is null)
        {
            return false;
        }

        var declared = Declared(accessor, owner);

        if(declaringType is AbstractType)
        {
            return BodilessReadonly(accessor, declared);
        }

        return declared.IsReadonly || accessor.IsAuto || Settle(accessor);
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

        return PurityJudge.ChainedOf(member) switch
        {
            Constructor chained => Unproven(chained, callees),
            ConcreteType chained => Unproven(chained, callees),
            _ => false,
        };
    }

    private bool Writes(object node, Origins origins, object member, List<object> callees)
    {
        return Triggers(node, member, callees) || node switch
        {
            AssignmentExpression assignment => Deconstructions.Pairs(assignment).Any(pair => Writes(pair.Target, origins, member) || StoresUnowned(origins, pair.Target, pair.Value)),
            ObjectInitializerAssignment entry => StoresUnowned(origins, entry) || EntryWrites(entry, callees),
            IndexExpression { Indexer: { } indexer } indexed =>
                CountWrites(indexed.FromEnd, origins.Of(indexed.Receiver), callees)
                || (!OwnLocalCallee(indexer.Getter, indexer.Modifiers, origins.Of(indexed.Receiver))
                    && UnprovenGetter(indexer.Getter, indexer.Modifiers, indexer.DeclaringType, callees)),
            IndexExpression { FromEnd: { } counted } indexed => CountWrites(counted, origins.Of(indexed.Receiver), callees),
            ListPattern { Reads: { } reads } => CountWrites(reads, Origin.External, callees)
                || UnprovenGetter(reads.Indexer.Getter, reads.Indexer.Modifiers, reads.Indexer.DeclaringType, callees),
            // An intrinsic operator is the target's own arithmetic, with no body that could write.
            BinaryExpression { Operator: { Modifiers.IsIntrinsic: false } binaryOperator } => Unproven(binaryOperator, callees),
            UnaryExpression { Operator: { Modifiers.IsIntrinsic: false } unaryOperator } => Unproven(unaryOperator, callees),
            CastExpression { Operator: { Modifiers.IsIntrinsic: false } conversion } => Unproven(conversion, callees),
            ImplicitConversionExpression { Operator: { Modifiers.IsIntrinsic: false } conversion } => Unproven(conversion, callees),
            CloneExpression clone => CloneWrites(clone, origins, callees),
            InvocationExpression { Method: null } call => CallsFunctionValue(call, origins, callees),
            InvocationExpression invocation => (!OwnLocalCallee(invocation, origins) && Unproven(invocation.Method, callees))
                || ComparesWriting(ElementComparisons.Of(invocation, out _), callees),
            BinaryExpression { Operator: null } comparison => ComparesWriting(ElementComparisons.OfAnonymous(comparison), callees),
            NewExpression { Constructor: { } constructor } => Unproven(constructor, callees),
            DefaultExpression fallback when PurityJudge.DefaultsTypeParameter(fallback) => true,
            DefaultExpression fallback => PurityJudge.DefaultChainOf(fallback) switch
            {
                Constructor chained => Unproven(chained, callees),
                ConcreteType chained => Unproven(chained, callees),
                _ => false,
            },
            NewExpression allocation when PurityJudge.AllocatesTypeParameter(allocation) => true,
            NewExpression allocation => PurityJudge.ImplicitConstructorOf(allocation) is { } type && Unproven(type, callees),
            PropertyReferenceExpression { Property: { } read } => UnprovenGetter(read.Getter, read.Modifiers, read.DeclaringType, callees),
            MemberAccessExpression { Member: PropertyResult { Property: { } accessed } } access =>
                !OwnLocalCallee(accessed.Getter, accessed.Modifiers, HolderOf(origins, access))
                && UnprovenGetter(accessed.Getter, accessed.Modifiers, accessed.DeclaringType, callees),
            ExtensionPropertyAccessExpression { Property: { } extended } extension =>
                !OwnLocalCallee(extended.Getter, extended.Modifiers, origins.Of(extension.Receiver))
                && UnprovenGetter(extended.Getter, extended.Modifiers, extended.DeclaringType, callees),
            PropertySubpattern { Member: PropertyResult { Property: { } tested } } =>
                UnprovenGetter(tested.Getter, tested.Modifiers, tested.DeclaringType, callees),
            ForEachStatement loop => LoopWrites(loop, origins, callees),
            _ => false,
        };
    }

    private bool Writes(Expression target, Origins origins, object member)
    {
        return target switch
        {
            _ when Target(target) is { } written => !Privileged(member, target, written) && HolderOf(origins, target) is not Origin.Own,
            ThisExpression => member is not (Constructor or ConcreteType) && origins.This is not Origin.Own,
            LocalReferenceExpression { Local.IsRef: true } => origins.Of(target) is not Origin.Own,
            InvocationExpression { Method.Modifiers.IsRef: true } => origins.OfReferent(target) is not Origin.Own,
            IndexExpression { Indexer: { } indexer } => HolderOf(origins, target) is not Origin.Own || !LocalSetter(indexer.Setter, indexer.Modifiers),
            IndexExpression => HolderOf(origins, target) is not Origin.Own,
            BackingFieldReferenceExpression or PropertyReferenceExpression => true,
            MemberAccessExpression { Member: PropertyResult { Property: var set } } => HolderOf(origins, target) is not Origin.Own || !LocalSetter(set.Setter, set.Modifiers),
            ExtensionPropertyAccessExpression { Property: var extended } => HolderOf(origins, target) is not Origin.Own || !LocalSetter(extended.Setter, extended.Modifiers),
            _ => false,
        };
    }

    private bool ComparesWriting(IReadOnlyList<Method>? compared, List<object> callees)
    {
        return compared is not null && compared.Any(callee => Unproven(callee, callees));
    }

    private bool CountWrites(CountedIndexer? counted, Origin holder, List<object> callees)
    {
        return counted is { Count: var count }
            && !OwnLocalCallee(count.Getter, count.Modifiers, holder)
            && UnprovenGetter(count.Getter, count.Modifiers, count.DeclaringType, callees);
    }

    private bool EntryWrites(ObjectInitializerAssignment entry, List<object> callees)
    {
        if(entry.Add is { } add)
        {
            return !IsLocalMember(add, add.Modifiers) && Unproven(add, callees);
        }

        return entry.Member is PropertyResult { Property: { Setter: { } setter } property }
            && !LocalSetter(setter, property.Modifiers)
            && UnprovenSetter(setter, property.Modifiers, callees);
    }

    private bool CloneWrites(CloneExpression clone, Origins origins, List<object> callees)
    {
        return PurityJudge.CloneOf(clone.Type) is { } method
            && !(origins.Of(clone.Source) is Origin.Own && IsLocalMember(method, method.Modifiers))
            && Unproven(method, callees);
    }

    private bool LoopWrites(ForEachStatement loop, Origins origins, List<object> callees)
    {
        var holder = origins.Of(loop.Collection);

        if(ClrCallbacks.IsUnrecorded(loop))
        {
            return true;
        }

        foreach(var method in new[] { loop.GetEnumerator, loop.MoveNext, ClrCallbacks.DisposeOf(loop) })
        {
            if(method is not null && !(holder is Origin.Own && IsLocalMember(method, method.Modifiers)) && Unproven(method, callees))
            {
                return true;
            }
        }

        return loop.Current is { } current
            && !OwnLocalCallee(current.Getter, current.Modifiers, holder)
            && UnprovenGetter(current.Getter, current.Modifiers, current.DeclaringType, callees);
    }

    public static bool StoresUnowned(Origins origins, Expression target, Expression value)
    {
        var owned = target switch
        {
            FieldReferenceExpression field => ThisTiers.IsOwned(field.Field),
            MemberAccessExpression { Member: FieldResult found } => ThisTiers.IsOwned(found.Field),
            PropertyReferenceExpression property => property.Property.Modifiers is { IsOwned: true, IsStatic: false },
            MemberAccessExpression { Member: PropertyResult found } => found.Property.Modifiers is { IsOwned: true, IsStatic: false },
            LocalReferenceExpression { Local.IsRef: true } local => origins.ReferentOf(local.Local) is { } referent && origins.IsOwnedSlot(referent),
            InvocationExpression { Method.Modifiers.IsRef: true } => true,
            _ => false,
        };

        return owned && HolderOf(origins, target) is Origin.Own && Unowned(origins, value);
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
        return getter is not null && !owner.IsStatic && holder is Origin.Own && IsLocalMember(getter, Declared(getter, owner));
    }

    private bool LocalSetter(PropertyAccessor? setter, MemberModifiers owner)
    {
        return setter is not null && IsLocalMember(setter, Declared(setter, owner));
    }

    public static Origin HolderOf(Origins origins, Expression target)
    {
        return target switch
        {
            MemberAccessExpression { Member: FieldResult { Field.Modifiers.IsStatic: true } or PropertyResult { Property.Modifiers.IsStatic: true } } => Origin.External,
            MemberAccessExpression access => origins.Of(access.Receiver),
            ExtensionPropertyAccessExpression extension => origins.Of(extension.Receiver),
            IndexExpression index => origins.Of(index.Receiver),
            LocalReferenceExpression local => origins.Of(local),
            InvocationExpression call => origins.OfReferent(call),
            _ => origins.This,
        };
    }

    private static bool Privileged(object member, Expression target, Field written)
    {
        var (built, statics) = member switch
        {
            Constructor constructor => (constructor.DeclaringType, constructor.Modifiers.IsStatic),
            ConcreteType type => (type, false),
            TypeInitializer { Type: var initialized } => (initialized, true),
            _ => (null, false),
        };

        return built is not null && ReferenceEquals(written.DeclaringType, built) && written.Modifiers.IsStatic == statics
            && (statics || target is FieldReferenceExpression or MemberAccessExpression { Receiver: ThisExpression });
    }

    private bool Unproven(Method? callee, List<object> callees)
    {
        if(callee is null)
        {
            return true;
        }

        if(callee.Modifiers.IsReadonly || callee is CastOperator { CopiesClone: true })
        {
            return false;
        }

        if(callee.Body is null || callee is { DeclaringType: AbstractType, Modifiers.IsStatic: false })
        {
            return !BodilessReadonly(callee, callee.Modifiers);
        }

        callees.Add(callee);
        return false;
    }

    private static bool Unproven(ConcreteType type, List<object> callees)
    {
        if(type.Modifiers.IsReadonly)
        {
            return false;
        }

        callees.Add(type);
        return false;
    }

    private bool UnprovenGetter(PropertyAccessor? getter, MemberModifiers owner, MemberType? declaringType, List<object> callees)
    {
        var declared = Declared(getter, owner);

        if(declared.IsReadonly)
        {
            return false;
        }

        if(getter is null)
        {
            return true;
        }

        if(declaringType is AbstractType || (!getter.IsAuto && getter.Body is null && getter.ExpressionBody is null))
        {
            return !BodilessReadonly(getter, declared);
        }

        if(getter.IsAuto)
        {
            return false;
        }

        callees.Add(getter);
        return false;
    }

    private bool UnprovenSetter(PropertyAccessor setter, MemberModifiers owner, List<object> callees)
    {
        var declared = Declared(setter, owner);

        if(declared.IsReadonly)
        {
            return false;
        }

        if(setter.IsAuto || (setter.Body is null && setter.ExpressionBody is null))
        {
            return !BodilessReadonly(setter, declared);
        }

        callees.Add(setter);
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

            case ConcreteType type:
                foreach(var initializer in PurityInference.ImplicitConstructorInitializers(type))
                {
                    yield return initializer;
                }

                break;

            case TypeInitializer initializer:
                foreach(var root in initializer.Initializers())
                {
                    yield return root;
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
            ExtensionPropertyAccessExpression extension => Rooted(extension.Receiver),
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