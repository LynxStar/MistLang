using System.Runtime.CompilerServices;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a member can hand out the object it runs on: its body -- for a construction, the whole chain
/// of constructors and the instance initializers they run -- uses `this` other than through a member
/// access on it, as an argument, a stored value, a capture or a method group, or calls a member of the
/// object that does. A construction that cannot is fresh when the caller receives it, and a local that
/// only calls such members keeps what it holds to itself. A declared pure member writes nothing outside
/// what it owns, so it cannot keep `this` anywhere; a method with no body here may do anything, while a
/// constructor with none is the runtime's and taken to build what it returns.
/// </summary>
internal static class ThisEscapes
{
    private static readonly ConditionalWeakTable<object, StrongBox<bool>> Known = [];

    public static bool Of(NewExpression allocation)
    {
        return (allocation.Constructor ?? (object?)PurityJudge.ImplicitConstructorOf(allocation)) is { } constructor && Leaks(constructor);
    }

    public static bool Leaks(object member)
    {
        return Known.GetValue(member, key => new StrongBox<bool>(Walk(key))).Value;
    }

    private static bool Walk(object start)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(start);

        while(pending.Count > 0)
        {
            var member = pending.Pop();

            if(!visited.Add(member) || IsPure(member))
            {
                continue;
            }

            if(member is (Method { Body: null } and not Constructor) or PropertyAccessor { IsAuto: false, Body: null, ExpressionBody: null })
            {
                return true;
            }

            foreach(var root in Roots(member))
            {
                if(Leaks(root, parent: null, pending))
                {
                    return true;
                }
            }

            if(PurityJudge.ChainedOf(member) is { } chained)
            {
                pending.Push(chained);
            }
        }

        return false;
    }

    private static bool IsPure(object member)
    {
        return member switch
        {
            Method method => method.Modifiers.IsPure,
            ConcreteType type => type.Modifiers.IsPure,
            PropertyAccessor accessor => accessor.OwnModifiers?.IsPure == true,
            _ => false,
        };
    }

    private static IEnumerable<object> Roots(object member)
    {
        switch(member)
        {
            case Constructor constructor:
                if(constructor.Body is { } body)
                {
                    yield return body;
                }

                foreach(var argument in constructor.ChainArgumentsList)
                {
                    yield return argument;
                }

                foreach(var initializer in PurityInference.InstanceInitializers(constructor))
                {
                    yield return initializer;
                }

                break;

            case ConcreteType type:
                foreach(var initializer in PurityInference.InstanceInitializers(type))
                {
                    yield return initializer;
                }

                break;

            case Method { Body: { } methodBody }:
                yield return methodBody;
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

    private static bool Leaks(object node, object? parent, Stack<object> pending)
    {
        switch(node)
        {
            case ThisExpression when !IsReceiverOf(parent, node):
                return true;

            case LambdaExpression or LocalFunctionStatement when BodyWalker.DescendantsAndSelf(node).Any(ReachesThis):
                return true;

            case FunctionReferenceExpression { Method.Modifiers.IsStatic: false, Receiver: null or ThisExpression }:
                return true;

            case InvocationExpression { Method: { Modifiers.IsStatic: false } method } call when ThisTiers.ReceiverOf(call) is null or ThisExpression:
                if(method.DeclaringType is null && ThisTiers.ReceiverOf(call) is ThisExpression && !method.Modifiers.IsPure)
                {
                    return true;
                }

                pending.Push(method);
                break;

            case MemberAccessExpression { Receiver: ThisExpression, Member: PropertyResult { Property: var property } }:
                Push(property.Getter, property.Setter, pending);
                break;

            case PropertyReferenceExpression { Property: { Modifiers.IsStatic: false } property }:
                Push(property.Getter, property.Setter, pending);
                break;

            case IndexExpression { Receiver: ThisExpression, Indexer: { } indexer }:
                Push(indexer.Getter, indexer.Setter, pending);
                break;

            case ExtensionPropertyAccessExpression { Receiver: ThisExpression, Property.Getter: var getter } when getter?.OwnModifiers?.IsPure != true:
                return true;
        }

        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            if(Leaks(child, node, pending))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReceiverOf(object? parent, object node)
    {
        return parent switch
        {
            MemberAccessExpression access => ReferenceEquals(access.Receiver, node),
            IndexExpression index => ReferenceEquals(index.Receiver, node),
            ExtensionPropertyAccessExpression extension => ReferenceEquals(extension.Receiver, node),
            _ => false,
        };
    }

    private static bool ReachesThis(object node)
    {
        return node is ThisExpression or FieldReferenceExpression { Field.Modifiers.IsStatic: false } or PropertyReferenceExpression { Property.Modifiers.IsStatic: false }
            or BackingFieldReferenceExpression
            || (node is InvocationExpression { Method: { Modifiers.IsStatic: false, DeclaringType: not null } } call && ThisTiers.ReceiverOf(call) is null);
    }

    private static void Push(PropertyAccessor? getter, PropertyAccessor? setter, Stack<object> pending)
    {
        foreach(var accessor in new[] { getter, setter })
        {
            if(accessor is { IsAuto: false })
            {
                pending.Push(accessor);
            }
        }
    }
}