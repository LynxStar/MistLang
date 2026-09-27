using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
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
/// </summary>
internal sealed class Origins
{
    private readonly Origins? enclosing;
    private readonly bool capturesAreInputs;
    private readonly bool isClosure;
    private readonly HashSet<LocalVariable> declared;
    private readonly HashSet<LocalVariable> own;
    private readonly HashSet<LocalVariable> fresh = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LocalVariable> aliased = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LocalVariable> assigned = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, Expression> refTargets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, Bound> bound = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, LambdaExpression> heldLambdas = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Method, LocalFunctionStatement> functions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Origin> captures = new(ReferenceEqualityComparer.Instance);

    public Origins(object root, object? member, ScopeKind kind, Origins? enclosing = null, bool capturesAreInputs = false)
    {
        Kind = kind;
        Member = member;
        this.enclosing = enclosing;
        this.capturesAreInputs = capturesAreInputs;
        isClosure = enclosing is not null && root is LambdaExpression or LocalFunctionStatement;

        var parameters = (isClosure, root) switch
        {
            (true, LambdaExpression lambda) => lambda.Parameters,
            (true, LocalFunctionStatement function) => function.Parameters,
            _ => [],
        };

        var inside = (isClosure, root) switch
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

    public Origin This => isClosure ? ThisOfClosure() : ThisOfMember();

    public Origin Of(Expression expression)
    {
        return expression switch
        {
            NewExpression allocation when IsView(allocation.Type) => Origin.External,
            NewExpression or ArrayCreationExpression or ArrayLiteralExpression or TupleLiteralExpression
                or AnonymousObjectExpression or CloneExpression or StackallocExpression or LambdaExpression
                or DefaultExpression or LiteralExpression or RegexLiteralExpression or InterpolatedStringExpression
                or VoidLiteralExpression or RangeExpression or TypeLiteralExpression or ErrorExpression
                or UnresolvedExpression or UnboundLambdaExpression => Origin.Own,
            TypeReferenceExpression or NamespaceReferenceExpression or MethodGroupExpression
                or MethodReferenceExpression or FunctionReferenceExpression => Origin.External,
            InvocationExpression call when ReturnsOwned(call) => Origin.Own,
            ImplicitConversionExpression { Operator: null } conversion => Of(conversion.Operand),
            CastExpression { Operator: null } cast => Of(cast.Operand),
            AsExpression converted => Of(converted.Operand),
            SpreadExpression spread => Of(spread.Operand),
            AssignmentExpression assignment => Of(assignment.Value),
            CoalesceExpression coalesce => Worst(Of(coalesce.Left), Of(coalesce.Right)),
            SwitchExpression chosen => chosen.Arms.Select(arm => Of(arm.Result)).Aggregate(Origin.Own, Worst),
            LocalReferenceExpression local => OfLocal(local.Local),
            ThisExpression => This,
            FieldReferenceExpression field => Through(This, field.Field, holderType: null),
            MemberAccessExpression { Member: FieldResult found } access => Through(Of(access.Receiver), found.Field, HolderTypeOf(access.Receiver)),
            MemberAccessExpression { Member: EnumMemberResult } => Origin.Own,
            BackingFieldReferenceExpression backing => Through(This, backing.Property, holderType: null),
            IndexExpression { Indexer: null } element => ThisTiers.IsValue(element.Type) ? Of(element.Receiver) : Origin.External,
            _ => ThisTiers.IsValue(expression.Type) && !IsView(expression.Type) ? Origin.Own : Origin.External,
        };
    }

    /// <summary>
    /// An owned reference field answers its holder's origin only through an owned REFERENCE -- an owned
    /// or fresh object, or the instance itself, which is what a null <paramref name="holderType"/> names.
    /// Through a struct held by value the answer is External: the copy's field still points at the one
    /// object every copy shares.
    /// </summary>
    public static Origin Through(Origin holder, Field field, IMistType? holderType)
    {
        if(field.Modifiers.IsStatic)
        {
            return Origin.External;
        }

        if(ThisTiers.IsValue(field.Type))
        {
            return holder;
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
            _ => type is StructType { IsRef: true },
        };
    }

    private static IMistType? HolderTypeOf(Expression receiver)
    {
        return receiver is ThisExpression ? null : receiver.Type;
    }

    public static Origin Worst(Origin left, Origin right)
    {
        return left > right ? left : right;
    }

    public bool IsOwnBinding(LocalVariable local)
    {
        return !isClosure || own.Contains(local);
    }

    public bool IsInput(LocalVariable local)
    {
        return isClosure ? own.Contains(local) && !declared.Contains(local) : !declared.Contains(local);
    }

    public bool ReadableCapture(LocalVariable local)
    {
        if(isClosure && !own.Contains(local))
        {
            return enclosing!.ReadableCapture(local);
        }

        return local.IsReadonly || (!aliased.Contains(local) && !assigned.Contains(local));
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
        return type switch
        {
            LiftedType lifted => IsUnchangeable(lifted.Type),
            UnionType union => union.Constituents.All(IsUnchangeable),
            INamedTupleType named => IsUnchangeable(named.Underlying),
            ITupleType tuple => tuple.ElementTypes.All(IsUnchangeable),
            _ => ThisTiers.IsValue(type) || Declarations.IsReadonly(type) || OptionalUnion.IsVoid(type) || IsString(type),
        };
    }

    private static bool IsString(IMistType type)
    {
        return string.Equals(type.FullyQualifiedName, WellKnownTypes.String.FullyQualifiedName, StringComparison.Ordinal);
    }

    private static bool ReturnsOwned(InvocationExpression call)
    {
        return call.Method is { } method && (method.Modifiers.IsOwned || method.ReturnOwnership == OwnershipKind.Owned);
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
            (ScopeKind.Pure, Constructor) => Origin.Own,
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
                return !local.IsRef && IsOwnCopy(local.Type) && ReadableCapture(local) ? Origin.Own : Origin.External;
            }

            return enclosing!.OfLocal(local);
        }

        return local switch
        {
            { Ownership: OwnershipKind.Owned } => Origin.Own,
            { Ownership: OwnershipKind.Frozen } => Origin.Frozen,
            { IsRef: true, Parameter: null } => refTargets.TryGetValue(local, out var target) ? Of(target) : Origin.External,
            { IsRef: true } => Origin.External,
            _ when bound.TryGetValue(local, out var source) => ThroughHops(Of(source.Scrutinee), source.Hops),
            _ when fresh.Contains(local) || IsOwnCopy(local.Type) => Origin.Own,
            _ => Origin.External,
        };
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

    private void Survey(IReadOnlyList<object> inside)
    {
        var values = new Dictionary<LocalVariable, List<Expression>>(ReferenceEqualityComparer.Instance);

        foreach(var node in inside)
        {
            switch(node)
            {
                case LocalDeclarationStatement declaration:
                    values[declaration.Local] = declaration.Initializer is { } initializer ? [initializer] : [];

                    if(declaration.Local.IsRef && declaration.Initializer is { } referent)
                    {
                        refTargets[declaration.Local] = referent;
                        MarkAliased(referent);
                    }

                    break;

                case WithBlockStatement { Binding: { } binding } ambient:
                    values[binding] = [ambient.Value];
                    break;

                case AssignmentExpression { Target: LocalReferenceExpression target } assignment:
                    _ = assigned.Add(target.Local);

                    if(values.TryGetValue(target.Local, out var list))
                    {
                        list.Add(assignment.Value);
                    }

                    break;

                case AssignmentExpression { Target: var target }:
                    MarkStructAssigned(ReceiverOf(target));
                    break;

                case InvocationExpression invocation:
                    MarkByRef(invocation.Arguments, invocation.Method?.ParametersList);
                    MarkStructAssigned(ThisTiers.ReceiverOf(invocation));
                    break;

                case NewExpression allocation:
                    MarkByRef(allocation.Arguments, allocation.Constructor?.ParametersList);
                    break;

                case LocalFunctionStatement function:
                    functions[function.Function] = function;
                    break;

                case IsPatternExpression test:
                    BindPattern(test.Pattern, test.Scrutinee, []);
                    break;

                case SwitchExpression chosen:
                    foreach(var arm in chosen.Arms)
                    {
                        BindPattern(arm.Pattern, chosen.Scrutinee, []);
                    }

                    break;
            }
        }

        foreach(var (local, _) in values)
        {
            if(!aliased.Contains(local) && !local.IsRef)
            {
                _ = fresh.Add(local);
            }
        }

        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var local in fresh.ToList())
            {
                if(values[local].Exists(value => Of(value) != Origin.Own))
                {
                    _ = fresh.Remove(local);
                    changed = true;
                }
            }
        }

        foreach(var (local, list) in values)
        {
            if(!assigned.Contains(local) && !aliased.Contains(local) && list is [var only] && Unwrapped(only) is LambdaExpression lambda)
            {
                heldLambdas[local] = lambda;
            }
        }
    }

    private void BindPattern(Pattern? pattern, Expression scrutinee, IReadOnlyList<Hop> hops)
    {
        if(pattern?.Binding is { } binding)
        {
            bound[binding] = new Bound(scrutinee, hops);
        }

        switch(pattern)
        {
            case PropertyPattern property:
                BindPattern(property.TypeTest, scrutinee, hops);

                foreach(var subpattern in property.Subpatterns)
                {
                    if(subpattern.Member is FieldResult { Field: var field })
                    {
                        BindPattern(subpattern.Pattern, scrutinee, [.. hops, new Hop(field, property.NarrowedType)]);
                    }
                }

                break;

            case ConjunctionPattern conjunction:
                foreach(var operand in conjunction.Operands)
                {
                    BindPattern(operand, scrutinee, hops);
                }

                break;

            case DisjunctionPattern disjunction:
                foreach(var operand in disjunction.Operands)
                {
                    BindPattern(operand, scrutinee, hops);
                }

                break;

            case NegatedPattern negated:
                BindPattern(negated.Operand, scrutinee, hops);
                break;

            case TypeTestPattern { Refinement: { } refinement }:
                BindPattern(refinement, scrutinee, hops);
                break;
        }
    }

    private static Expression Unwrapped(Expression expression)
    {
        return expression is ImplicitConversionExpression { Operator: null } conversion ? Unwrapped(conversion.Operand) : expression;
    }

    private void MarkByRef(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        if(parameters is null)
        {
            return;
        }

        for(var i = 0; i < arguments.Count && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.IsRef)
            {
                MarkAliased(arguments[i]);
            }
        }
    }

    private void MarkAliased(Expression argument)
    {
        if(argument is LocalReferenceExpression reference)
        {
            _ = aliased.Add(reference.Local);
        }
    }

    private void MarkStructAssigned(Expression? receiver)
    {
        if(receiver is not null && StructRoot(receiver) is { } root)
        {
            _ = assigned.Add(root);
        }
    }

    private static LocalVariable? StructRoot(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression reference when ThisTiers.IsValue(reference.Local.Type) => reference.Local,
            MemberAccessExpression { Member: FieldResult } access when ThisTiers.IsValue(access.Receiver.Type) => StructRoot(access.Receiver),
            IndexExpression { Indexer: null } index when ThisTiers.IsValue(index.Receiver.Type) => StructRoot(index.Receiver),
            _ => null,
        };
    }

    private static Expression? ReceiverOf(Expression target)
    {
        return target switch
        {
            MemberAccessExpression access => access.Receiver,
            IndexExpression index => index.Receiver,
            ExtensionPropertyAccessExpression extension => extension.Receiver,
            _ => null,
        };
    }

    private sealed record Bound(Expression Scrutinee, IReadOnlyList<Hop> Hops);

    private readonly record struct Hop(Field Field, IMistType HolderType);
}