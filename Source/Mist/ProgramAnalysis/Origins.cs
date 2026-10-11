using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

internal enum Origin
{
    Own,
    Frozen,
    External,
}

internal enum ScopeKind
{
    Pure,
    Local,
    LocalReadonly,
    Readonly,
}

/// <summary>
/// Where a value comes from, for the scope judging it: what the scope owns, what it holds frozen, and
/// everything else (purity spec, design 'Origins'). Read off the body WITHOUT flow: a local is owned
/// when every assignment to it is fresh and nothing aliases it, so the answer holds at every use. A
/// closure's Origins is built over its enclosing scope's, which answers what a capture reaches and
/// whether the captured variable may be read at all (Requirement 16.4) -- unless the closure is judged
/// as a conversion to a pure function type, where every capture is an External input. A closure given
/// with no enclosing scope is a body root of its own -- an initializer or an expression body that IS a
/// lambda -- whose parameters are inputs like a member's.
/// <para>
/// An owned reference field answers its holder's origin only through an owning reference, which a null
/// holder type passed to <see cref="Through(Origin, Field, IMistType)"/> also names: a struct's own
/// member is handed a copy, whose fields share their objects with every other copy, so only its
/// constructor, building the one instance, holds its owned fields as its own. A readonly type is not
/// thereby unchangeable: it promises that no member writes it, not that nothing it holds can change, so
/// an owned field of a mutable class or a settable property's storage (readonly-contexts base rule 12)
/// is state a second reference can still reach. An intrinsic type has no fields here to look at, so its
/// declaration is taken at its word; an abstract one could be implemented by anything.
/// </para>
/// </summary>
internal sealed partial class Origins
{
    private readonly Origins? enclosing;
    private readonly bool capturesAreInputs;
    private readonly IMistType? thisType;
    private readonly HashSet<LocalVariable> declared;
    private readonly HashSet<LocalVariable> own;
    private readonly HashSet<LocalVariable> fresh = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LocalVariable> aliased = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LocalVariable> assigned = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LocalVariable> escaped = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, Expression> refTargets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, Bound> bound = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, Expression> leased = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, LambdaExpression> heldLambdas = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Method, LocalFunctionStatement> functions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Origin> captures = new(ReferenceEqualityComparer.Instance);

    public Origins(object root, object? member, ScopeKind kind, Origins? enclosing = null, bool capturesAreInputs = false)
    {
        Kind = kind;
        Member = member;
        this.enclosing = enclosing;
        this.capturesAreInputs = capturesAreInputs;
        IsClosure = enclosing is not null && root is LambdaExpression or LocalFunctionStatement;
        thisType = enclosing is not null ? enclosing.thisType : ThisTypeOf(member);

        var parameters = (IsClosure, root) switch
        {
            (true, LambdaExpression lambda) => lambda.Parameters,
            (true, LocalFunctionStatement function) => function.Parameters,
            _ => [],
        };

        var inside = (IsClosure, root) switch
        {
            (true, LambdaExpression lambda) => Captures.Inside(lambda),
            (true, LocalFunctionStatement function) => Captures.Inside(function),
            _ => BodyWalker.DescendantsAndSelf(root).ToList(),
        };

        declared = Captures.OwnNames([], inside);
        own = Captures.OwnNames(parameters, inside);
        Survey(inside);
    }

    public ScopeKind Kind { get; }

    public object? Member { get; }

    public bool IsClosure { get; }

    public Origin This => IsClosure ? ThisOfClosure() : ThisOfMember();

    public Origin Of(Expression expression)
    {
        return expression switch
        {
            NewExpression allocation when IsView(allocation.Type) => Origin.External,
            NewExpression allocation => ThisTiers.IsValue(allocation.Type) || !ThisEscapes.Of(allocation) ? Origin.Own : Origin.External,
            CloneExpression clone => OfClone(clone),
            ArrayCreationExpression or ArrayLiteralExpression or TupleLiteralExpression
                or AnonymousObjectExpression or StackallocExpression or LambdaExpression
                or DefaultExpression or LiteralExpression or RegexLiteralExpression or InterpolatedStringExpression
                or VoidLiteralExpression or RangeExpression or TypeLiteralExpression or ErrorExpression
                or UnresolvedExpression or UnboundLambdaExpression or UnboundContextualExpression => Origin.Own,
            TypeReferenceExpression or NamespaceReferenceExpression or MethodGroupExpression
                or MethodReferenceExpression or FunctionReferenceExpression => Origin.External,
            InvocationExpression call when ReturnsOwned(call) => OfOwnedReturn(call),
            InvocationExpression { Signature.Modifiers.IsRef: true } or IndexExpression { Indexer.Modifiers.IsRef: true }
                or MemberAccessExpression { Member: PropertyResult { Property.Modifiers.IsRef: true } }
                or PropertyReferenceExpression { Property.Modifiers.IsRef: true } => OfReferent(expression),
            ImplicitConversionExpression { Operator: null } conversion => Of(conversion.Operand),
            ImplicitConversionExpression { Operator: { } called } conversion when OwnedProvenance.Yields(called) => OfOwnedConversion(called, conversion.Operand),
            CastExpression { Operator: null } cast => Narrows(cast.Operand.Type, cast.Type) ? Origin.External : Of(cast.Operand),
            CastExpression { Operator: { } called } cast when OwnedProvenance.Yields(called) => OfOwnedConversion(called, cast.Operand),
            AsExpression converted => Narrows(converted.Operand.Type, converted.Type) ? Origin.External : Of(converted.Operand),
            SpreadExpression spread => Of(spread.Operand),
            AssignmentExpression assignment => Of(assignment.Value),
            CoalesceExpression coalesce => Worst(Of(coalesce.Left), Of(coalesce.Right)),
            SwitchExpression chosen => chosen.Arms.Select(arm => Of(arm.Result)).Aggregate(Origin.Own, Worst),
            LocalReferenceExpression local => OfLocal(local.Local),
            ThisExpression => This,
            FieldReferenceExpression field => Through(This, field.Field, thisType),
            MemberAccessExpression { Member: FieldResult found } access => Through(Of(access.Receiver), found.Field, HolderTypeOf(access.Receiver)),
            MemberAccessExpression { Member: EnumMemberResult } variant => IsUnchangeable(variant.Type) ? Origin.Own : Origin.External,
            BackingFieldReferenceExpression backing => Through(This, backing.Property, thisType),
            IndexExpression { Indexer: null } element => ThisTiers.IsValue(element.Type) ? Of(element.Receiver) : Origin.External,
            _ => ThisTiers.IsValue(expression.Type) && !IsView(expression.Type) ? Origin.Own : Origin.External,
        };
    }

    public static Origin Through(Origin holder, Field field, IMistType? holderType)
    {
        if(field.Modifiers.IsStatic)
        {
            return Origin.External;
        }

        if(ThisTiers.IsValue(field.Type))
        {
            return IsView(field.Type) ? Origin.External : holder;
        }

        return ThisTiers.IsOwned(field) && (holderType is null || !ThisTiers.IsValue(holderType)) ? holder : Origin.External;
    }

    public static Origin Through(Origin holder, Property property, IMistType? holderType)
    {
        if(property.Modifiers.IsStatic)
        {
            return Origin.External;
        }

        if(ThisTiers.IsValue(property.Type))
        {
            return holder;
        }

        return property.Modifiers.IsOwned && (holderType is null || !ThisTiers.IsValue(holderType)) ? holder : Origin.External;
    }

    public static bool IsView(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => IsView(aliased),
            RefinedType refined => IsView(refined.BaseType),
            ConstructedType constructed => IsView(constructed.Definition),
            _ => type is StructType { IsRef: true } or StructType { IsIntrinsic: true, Modifiers.IsReadonly: false },
        };
    }

    private IMistType? HolderTypeOf(Expression receiver)
    {
        return receiver is ThisExpression ? thisType : receiver.Type;
    }

    private static IMistType? ThisTypeOf(object? member)
    {
        var declaring = member switch
        {
            Constructor => null,
            Method method => method.DeclaringType,
            Property property => property.DeclaringType,
            Indexer indexer => indexer.DeclaringType,
            _ => null,
        };

        return declaring is { } type && ThisTiers.IsValue(type) ? type : null;
    }

    public static Origin Worst(Origin left, Origin right)
    {
        return left > right ? left : right;
    }

    public bool InitializesStatics(MistType? declaring)
    {
        return declaring is not null && Member switch
        {
            TypeInitializer initializer => ReferenceEquals(initializer.Type, declaring),
            Constructor { Modifiers.IsStatic: true } constructor => ReferenceEquals(constructor.DeclaringType, declaring),
            _ => false,
        };
    }

    public bool IsOwnBinding(LocalVariable local)
    {
        return !IsClosure || own.Contains(local);
    }

    public bool IsInput(LocalVariable local)
    {
        return IsClosure ? own.Contains(local) && !declared.Contains(local) : !declared.Contains(local);
    }

    public bool ReadableCapture(LocalVariable local)
    {
        if(IsClosure && !own.Contains(local))
        {
            return enclosing!.ReadableCapture(local);
        }

        return !assigned.Contains(local) && (local.IsReadonly || !aliased.Contains(local));
    }

    public Origin OfCaptures(object closure)
    {
        if(captures.TryGetValue(closure, out var known))
        {
            return known;
        }

        var origin = OfCaptures(closure, new HashSet<object>(ReferenceEqualityComparer.Instance));
        captures[closure] = origin;
        return origin;
    }

    private Origin OfCaptures(object closure, HashSet<object> visited)
    {
        if(!visited.Add(closure))
        {
            return Origin.Own;
        }

        var (parameters, inside) = closure switch
        {
            LambdaExpression lambda => (lambda.Parameters, Captures.Inside(lambda)),
            LocalFunctionStatement function => (function.Parameters, Captures.Inside(function)),
            _ => throw new InvalidOperationException($"ICE: {closure.GetType().Name} is not a closure."),
        };

        var names = Captures.OwnNames(parameters, inside);
        var origin = Origin.Own;

        foreach(var node in inside)
        {
            var captured = node switch
            {
                LocalReferenceExpression reference when !names.Contains(reference.Local) => OfLocal(reference.Local),
                ThisExpression or FieldReferenceExpression or PropertyReferenceExpression or BackingFieldReferenceExpression => This,
                InvocationExpression { Method: { DeclaringType: not null, Modifiers.IsStatic: false } } call when ThisTiers.ReceiverOf(call) is null => This,
                InvocationExpression { Method: { DeclaringType: null } called } when FunctionOf(called) is { } function => OfCaptures(function, visited),
                InvocationExpression { Method: null, Receiver: LocalReferenceExpression { Local: var holder } } when LambdaHeldBy(holder) is { } held => OfCaptures(held, visited),
                _ => Origin.Own,
            };

            origin = Worst(origin, captured);
        }

        return origin;
    }

    public LocalFunctionStatement? FunctionOf(Method method)
    {
        return functions.TryGetValue(method, out var function) ? function : enclosing?.FunctionOf(method);
    }

    public LambdaExpression? LambdaHeldBy(LocalVariable local)
    {
        return heldLambdas.TryGetValue(local, out var lambda) ? lambda : enclosing?.LambdaHeldBy(local);
    }

    public static bool IsUnchangeable(IMistType type)
    {
        return IsUnchangeable(type, new HashSet<MemberType>(ReferenceEqualityComparer.Instance));
    }

    private static bool IsUnchangeable(IMistType type, HashSet<MemberType> visiting)
    {
        return type switch
        {
            LiftedType lifted => IsUnchangeable(lifted.Type, visiting),
            UnionType union => union.Constituents.All(constituent => IsUnchangeable(constituent, visiting)),
            INamedTupleType named => IsUnchangeable(named.Underlying, visiting),
            ITupleType tuple => tuple.ElementTypes.All(element => IsUnchangeable(element, visiting)),
            TypeAlias { AliasedType: { } aliased } => IsUnchangeable(aliased, visiting),
            RefinedType refined => IsUnchangeable(refined.BaseType, visiting),
            Newtype { SourceType: { } source } => IsUnchangeable(source, visiting),
            _ when ThisTiers.IsValue(type) => !IsView(type),
            EnumType { UnderlyingType: { } underlying } => IsUnchangeable(underlying, visiting),
            _ when OptionalUnion.IsVoid(type) || IsString(type) || type is EnumType => true,
            _ => Declarations.IsReadonly(type) && HoldsNothingChangeable(type, visiting),
        };
    }

    private static bool HoldsNothingChangeable(IMistType type, HashSet<MemberType> visiting)
    {
        var constructed = type as ConstructedType;

        switch(constructed?.Definition ?? type)
        {
            case ConcreteType { IsIntrinsic: true }:
                return true;

            case ConcreteType concrete when visiting.Add(concrete):
                var fields = concrete.FieldsList.Where(field => !field.Modifiers.IsStatic).Select(field => field.Type);
                var properties = concrete.PropertiesList.Where(property => property is { HasBackingStorage: true, Modifiers.IsStatic: false }).ToList();

                return properties.TrueForAll(property => property.Setter is null or { IsInit: true })
                    && fields.Concat(properties.Select(property => property.Type))
                        .All(held => IsUnchangeable(Closed(held, concrete, constructed), visiting));

            case ConcreteType:
                return true;

            default:
                return false;
        }
    }

    private static IMistType Closed(IMistType declared, ConcreteType definition, ConstructedType? constructed)
    {
        var index = declared is TypeParameter parameter ? definition.TypeParametersList.IndexOf(parameter) : -1;

        return constructed is not null && index >= 0 && index < constructed.TypeArguments.Count ? constructed.TypeArguments[index] : declared;
    }

    private static bool IsString(IMistType type)
    {
        return string.Equals(type.FullyQualifiedName, WellKnownTypes.String.FullyQualifiedName, StringComparison.Ordinal);
    }

    private static bool ReturnsOwned(InvocationExpression call)
    {
        return call.Method is { } method && ReturnsOwned(method);
    }

    private static bool ReturnsOwned(Method method)
    {
        return method.Modifiers.IsOwned || method.ReturnOwnership == OwnershipKind.Owned;
    }

    private Origin OfOwnedReturn(InvocationExpression call)
    {
        if(call.Method is not { DeclaredThisTier: ThisTier.Owned, Modifiers.IsStatic: false })
        {
            return Origin.Own;
        }

        return ThisTiers.ReceiverOf(call) is { } receiver ? Of(receiver) : This;
    }

    private Origin OfOwnedConversion(CastOperator called, Expression operand)
    {
        return called.ClonesThrough switch
        {
            null => Origin.Own,
            { DeclaredThisTier: ThisTier.Owned } method when ReturnsOwned(method) => Of(operand),
            { } method when ReturnsOwned(method) => Origin.Own,
            _ => Origin.External,
        };
    }

    private Origin OfClone(CloneExpression clone)
    {
        if(IsOwnCopy(clone.Type))
        {
            return Origin.Own;
        }

        return PurityJudge.CloneOf(clone.Type) switch
        {
            { DeclaredThisTier: ThisTier.Owned } method when ReturnsOwned(method) => Of(clone.Source),
            { } method when ReturnsOwned(method) => Origin.Own,
            _ => Origin.External,
        };
    }

    public static bool Narrows(IMistType from, IMistType to)
    {
        return IsUnchangeable(from) && !IsUnchangeable(to);
    }

    public Expression? ReferentOf(LocalVariable local)
    {
        return refTargets.TryGetValue(local, out var referent) ? referent : null;
    }

    public bool IsOwnedSlot(Expression referent)
    {
        return Unwrapped(referent) switch
        {
            FieldReferenceExpression field => ThisTiers.IsOwned(field.Field),
            MemberAccessExpression { Member: FieldResult found } => ThisTiers.IsOwned(found.Field),
            MemberAccessExpression { Member: PropertyResult { Property.Modifiers: var modifiers } } => modifiers.IsRef || modifiers is { IsOwned: true, IsStatic: false },
            PropertyReferenceExpression { Property.Modifiers: var modifiers } => modifiers.IsRef || modifiers is { IsOwned: true, IsStatic: false },
            InvocationExpression { Signature.Modifiers.IsRef: true } or IndexExpression { Indexer.Modifiers.IsRef: true } => true,
            LocalReferenceExpression { Local: { IsRef: true } local } => ReferentOf(local) is { } inner && IsOwnedSlot(inner),
            _ => false,
        };
    }

    public bool Escapes(LocalVariable local)
    {
        return escaped.Contains(local);
    }

    private bool InstanceIsOwn => Kind is ScopeKind.Local or ScopeKind.LocalReadonly;

    private Origin ThisOfClosure()
    {
        if(InstanceIsOwn)
        {
            return Origin.Own;
        }

        return capturesAreInputs ? Origin.External : enclosing!.This;
    }

    private Origin ThisOfMember()
    {
        if(InstanceIsOwn)
        {
            return Origin.Own;
        }

        return (Kind, Member) switch
        {
            (ScopeKind.Pure, Constructor or ConcreteType) => Origin.Own,
            (ScopeKind.Pure, Method { DeclaredThisTier: ThisTier.Frozen }) => Origin.Frozen,
            _ => Origin.External,
        };
    }

    private Origin OfLocal(LocalVariable local)
    {
        if(!IsOwnBinding(local))
        {
            if(InstanceIsOwn)
            {
                return Origin.Own;
            }

            if(capturesAreInputs)
            {
                return !local.IsRef && IsOwnCopy(local.Type) && ReadableCapture(local) ? Origin.Frozen : Origin.External;
            }

            return enclosing!.OfLocal(local);
        }

        return local switch
        {
            { Ownership: OwnershipKind.Owned } => Origin.Own,
            { Ownership: OwnershipKind.Frozen } when assigned.Contains(local) => Origin.External,
            { Ownership: OwnershipKind.Frozen } => leased.TryGetValue(local, out var lender) ? Worst(Origin.Frozen, Of(lender)) : Origin.Frozen,
            { IsRef: true, Parameter: null } => refTargets.TryGetValue(local, out var target) ? OfReferent(target) : Origin.External,
            { IsRef: true } => Origin.External,
            { Parameter.IsFlattened: true } when local.Type is not ITupleType && assigned.Contains(local) => Origin.External,
            { Parameter.IsFlattened: true } => local.Type is ITupleType ? Origin.Own : Origin.Frozen,
            _ when bound.TryGetValue(local, out var source) && !assigned.Contains(local) && !aliased.Contains(local) =>
                source.Narrowed ? Origin.External : source.Unboxed ? Origin.Own : ThroughHops(Of(source.Scrutinee), source.Hops),
            _ when bound.ContainsKey(local) => IsOwnCopy(local.Type) ? Origin.Own : Origin.External,
            _ when fresh.Contains(local) || IsOwnCopy(local.Type) => Origin.Own,
            _ => Origin.External,
        };
    }

    public Origin OfReferent(Expression referent)
    {
        var (byRef, isStatic, receiver) = Unwrapped(referent) switch
        {
            InvocationExpression { Signature: { } method } call => (method.Modifiers.IsRef, method.Modifiers.IsStatic, call.Method is null ? call.Receiver : ThisTiers.ReceiverOf(call)),
            IndexExpression { Indexer: { } indexer } index => (indexer.Modifiers.IsRef, false, index.Receiver),
            MemberAccessExpression { Member: PropertyResult { Property: var property } } access => (property.Modifiers.IsRef, property.Modifiers.IsStatic, access.Receiver),
            PropertyReferenceExpression { Property: var property } => (property.Modifiers.IsRef, property.Modifiers.IsStatic, null),
            _ => (false, false, null),
        };

        if(!byRef)
        {
            return Of(referent);
        }

        if(isStatic || Kind is ScopeKind.Readonly)
        {
            return Origin.External;
        }

        return Worst(receiver is null ? This : Of(receiver), Lent(Unwrapped(referent)));
    }

    private Origin Lent(Expression call)
    {
        var (arguments, parameters) = call switch
        {
            InvocationExpression invocation => (invocation.Arguments, invocation.Parameters),
            IndexExpression index => ([index.Index], null),
            _ => ([], null),
        };

        var origin = Origin.Own;

        for(var i = 0; i < arguments.Count; i++)
        {
            if(!IsUnchangeable(arguments[i].Type) || (parameters is not null && i < parameters.Count && parameters[i].Modifier.IsRef))
            {
                origin = Worst(origin, Of(arguments[i]));
            }
        }

        return origin;
    }

    private static bool IsOwnCopy(IMistType type)
    {
        return ThisTiers.IsValue(type) && !IsView(type);
    }

    private static Origin ThroughHops(Origin origin, IReadOnlyList<Hop> hops)
    {
        foreach(var (field, holderType) in hops)
        {
            origin = Through(origin, field, holderType);
        }

        return origin;
    }
}