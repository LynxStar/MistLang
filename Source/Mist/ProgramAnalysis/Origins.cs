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
    private readonly object? member;
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
        this.member = member;
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

    public Origin This => isClosure ? ThisOfClosure() : ThisOfMember();

    public Origin Of(Expression expression)
    {
        return expression switch
        {
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
            FieldReferenceExpression field => Through(This, field.Field),
            MemberAccessExpression { Member: FieldResult found } access => Through(Of(access.Receiver), found.Field),
            MemberAccessExpression { Member: EnumMemberResult } => Origin.Own,
            BackingFieldReferenceExpression backing => Through(This, backing.Property),
            IndexExpression { Indexer: null } element => ThisTiers.IsValue(element.Type) ? Of(element.Receiver) : Origin.External,
            _ => ThisTiers.IsValue(expression.Type) ? Origin.Own : Origin.External,
        };
    }

    public static Origin Through(Origin holder, Field field)
    {
        if(field.Modifiers.IsStatic)
        {
            return Origin.External;
        }

        return ThisTiers.IsValue(field.Type) || ThisTiers.IsOwned(field) ? holder : Origin.External;
    }

    public static Origin Through(Origin holder, Property property)
    {
        if(property.Modifiers.IsStatic)
        {
            return Origin.External;
        }

        return ThisTiers.IsValue(property.Type) || property.Modifiers.IsOwned ? holder : Origin.External;
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
                _ => Origin.Own,
            };

            origin = Worst(origin, captured);
        }

        captures[closure] = origin;
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
        return call.Method is { } method
            && (method.Modifiers.IsOwned
                || method.ReturnOwnership == OwnershipKind.Owned
                || (method.Name == "Clone" && method.ParametersList.Count == 0));
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

        return (Kind, member) switch
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
                return !local.IsRef && ThisTiers.IsValue(local.Type) ? Origin.Own : Origin.External;
            }

            return enclosing!.OfLocal(local);
        }

        return local switch
        {
            { Ownership: OwnershipKind.Owned } => Origin.Own,
            { Ownership: OwnershipKind.Frozen } => Origin.Frozen,
            { IsRef: true, Parameter: null } => refTargets.TryGetValue(local, out var target) ? Of(target) : Origin.External,
            { IsRef: true } => Origin.External,
            _ when bound.TryGetValue(local, out var source) => source.Hops.Aggregate(Of(source.Scrutinee), Through),
            _ when fresh.Contains(local) || (ThisTiers.IsValue(local.Type) && !local.IsRef) => Origin.Own,
            _ => Origin.External,
        };
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

                case InvocationExpression invocation:
                    MarkByRef(invocation.Arguments, invocation.Method?.ParametersList);
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

    private void BindPattern(Pattern? pattern, Expression scrutinee, IReadOnlyList<Field> hops)
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
                        BindPattern(subpattern.Pattern, scrutinee, [.. hops, field]);
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

    private sealed record Bound(Expression Scrutinee, IReadOnlyList<Field> Hops);
}