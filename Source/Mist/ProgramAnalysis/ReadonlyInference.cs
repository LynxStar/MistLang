using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a member reads only, or writes storage. Beside <see cref="ThisTiers"/> and for the same
/// reason: both answer what a BODY does, and one walk with two questions beats two walkers that can
/// disagree. The rules are the readonly-contexts spec's base rules (Req 11) read as a classification --
/// a field written, a setter called, a callee that is not readonly, an allocation through a constructor
/// that is not -- with a constructor privileged to assign the fields of its own type (Req 10.7).
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
internal sealed class ReadonlyInference
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

    private static bool Writes(object member, List<object> callees)
    {
        foreach(var node in Roots(member).SelectMany(BodyWalker.DescendantsAndSelf))
        {
            switch(node)
            {
                case AssignmentExpression assignment when Target(assignment.Target) is { } written:
                    if(!Privileged(member, written))
                    {
                        return true;
                    }

                    break;

                case AssignmentExpression indexed when IndexedWriteTargetName(indexed.Target) is not null:
                    return true;

                case AssignmentExpression { Target: BackingFieldReferenceExpression or PropertyReferenceExpression }:
                case AssignmentExpression { Target: MemberAccessExpression { Member: PropertyResult } }:
                    return true;

                case InvocationExpression invocation:
                    if(Unproven(invocation.Method, callees))
                    {
                        return true;
                    }

                    break;

                case NewExpression allocation:
                    if(Unproven(allocation.Constructor, callees))
                    {
                        return true;
                    }

                    break;

                case PropertyReferenceExpression { Property: { } read }:
                    if(UnprovenGetter(read.Modifiers.IsReadonly, read.Getter, callees))
                    {
                        return true;
                    }

                    break;

                case MemberAccessExpression { Member: PropertyResult { Property: { } accessed } }:
                    if(UnprovenGetter(accessed.Modifiers.IsReadonly, accessed.Getter, callees))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
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