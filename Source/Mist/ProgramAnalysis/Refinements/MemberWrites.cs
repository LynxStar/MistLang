using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// The fields a call may write, member by member: those of its receiver and the statics its callee's body
/// assigns, directly or through a ref local, with the bodies it calls on the same receiver or statically.
/// Purity Requirement 3 lets a callee that writes nothing External touch nothing else, so any other effect,
/// a ref local whose storage cannot be named, or a reference the call is handed that the callee may keep
/// (stored, returned, captured by a closure or a delegate, or handed on to a callee that may keep it) makes the
/// answer null: the call may write whatever it can reach, then or later through what it kept.
/// </summary>
internal sealed class MemberWrites(FieldEffects effects)
{
    public static readonly IReadOnlySet<Field> Nothing = new HashSet<Field>();

    private static readonly object This = new();

    private readonly Dictionary<Method, IReadOnlySet<Field>?> closures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Method, (HashSet<Field> Fields, List<Method> Callees)?> bodies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Method, HashSet<object>?> kept = new(ReferenceEqualityComparer.Instance);

    public IReadOnlySet<Field>? Of(object node)
    {
        if(!effects.MayWrite(node))
        {
            return Nothing;
        }

        return node is InvocationExpression call && Callee(call) is { } callee && !KeepsAny(call) ? Closure(callee) : null;
    }

    public bool Keeps(object node, Expression argument)
    {
        if(Origins.IsUnchangeable(argument.Type))
        {
            return false;
        }

        var arguments = node switch
        {
            InvocationExpression call when ReferenceEquals(ThisTiers.ReceiverOf(call), argument) => null,
            InvocationExpression call => call.Arguments,
            NewExpression creation => creation.Arguments,
            _ => [],
        };

        return Keeps(node, arguments is null ? -1 : IndexOf(arguments, argument));
    }

    private bool KeepsAny(InvocationExpression call)
    {
        return (ThisTiers.ReceiverOf(call) is { } receiver && Keeps(call, receiver)) || call.Arguments.Any(argument => Keeps(call, argument));
    }

    private static Method? CalleeOf(object node)
    {
        return node switch
        {
            InvocationExpression call => Callee(call),
            NewExpression { Constructor: { } constructor } => constructor.SourceDeclaration ?? constructor,
            _ => null,
        };
    }

    private bool Keeps(object node, int slot)
    {
        var callee = CalleeOf(node);
        if(callee is null)
        {
            return true;
        }

        var block = callee.DeclaringBlock ?? callee.ClosedBlock;
        if(slot < 0 && block is null && callee.Modifiers.IsStatic)
        {
            return false;
        }

        var root = slot switch
        {
            < 0 => block is null ? This : block.Receiver,
            _ when slot < callee.ParametersList.Count => callee.ParametersList[slot],
            _ => null,
        };

        return root is null || Kept(callee) is not { } roots || roots.Contains(root);
    }

    private HashSet<object>? Kept(Method method)
    {
        if(kept.TryGetValue(method, out var known))
        {
            return known;
        }

        var reached = new List<Method>();
        var pending = new Stack<Method>();
        pending.Push(method);
        while(pending.TryPop(out var next))
        {
            if(kept.ContainsKey(next))
            {
                continue;
            }

            var readable = next.Body is not null && !next.Modifiers.IsAsync && next is not { DeclaringType: AbstractType, Modifiers.IsStatic: false }
                and not Constructor { ChainArgumentsList.Count: > 0 };
            kept[next] = readable ? new HashSet<object>(ReferenceEqualityComparer.Instance) : null;
            if(readable)
            {
                reached.Add(next);
                foreach(var callee in BodyWalker.DescendantsAndSelf(next.Body!).Select(CalleeOf).OfType<Method>())
                {
                    pending.Push(callee);
                }
            }
        }

        for(var changed = true; changed;)
        {
            changed = false;
            foreach(var next in reached)
            {
                var roots = kept[next]!;
                var count = roots.Count;
                roots.UnionWith(Roots(next));
                changed |= roots.Count != count;
            }
        }

        return kept[method];
    }

    private HashSet<object> Roots(Method method)
    {
        var self = (method.DeclaringBlock ?? method.ClosedBlock)?.Receiver;
        var parameters = method.ParametersList.ToHashSet<object>(ReferenceEqualityComparer.Instance);
        object? root(object node)
        {
            return node switch
            {
                LocalReferenceExpression { Local: var local } when self is not null && ReferenceEquals(local, self) => local,
                LocalReferenceExpression { Local.Parameter: { } parameter } when parameters.Contains(parameter) => parameter,
                _ => NamesThis(node) ? This : null,
            };
        }

        var nodes = BodyWalker.DescendantsAndSelf(method.Body!).ToList();
        var closed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var safe = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void unless(bool keeps, object? reference)
        {
            if(!keeps && reference is not null)
            {
                _ = safe.Add(reference);
            }
        }

        foreach(var node in nodes)
        {
            switch(node)
            {
                case LambdaExpression lambda:
                    closed.UnionWith(Captures.Inside(lambda));
                    break;
                case LocalFunctionStatement function:
                    closed.UnionWith(Captures.Inside(function));
                    break;
                case AsyncBlockExpression block:
                    closed.UnionWith(BodyWalker.DescendantsAndSelf(block.Body));
                    break;
                case AsyncExpression deferred:
                    closed.UnionWith(BodyWalker.DescendantsAndSelf(deferred.Operand));
                    break;
                case FieldReferenceExpression or BackingFieldReferenceExpression:
                    _ = safe.Add(node);
                    break;
                case MemberAccessExpression { Member: FieldResult } access:
                    _ = safe.Add(access.Receiver);
                    break;
                case MemberAccessExpression { Member: PropertyResult { Property: var property } } access:
                    unless(!Reads(access, property), access.Receiver);
                    break;
                case PropertyReferenceExpression { Property: var property } reference:
                    unless(!Reads(reference, property), reference);
                    break;
                case AssignmentExpression { Target: LocalReferenceExpression { Local: var local } target } assignment
                    when(!local.IsRef && local.Parameter?.Modifier is null or { IsRef: false, Direction: ByRefDirection.None })
                        || assignment.Value is LiteralExpression || Origins.IsUnchangeable(assignment.Value.Type):
                    _ = safe.Add(target);
                    break;
                case InvocationExpression call:
                    unless(Keeps(call, -1), ThisTiers.ReceiverOf(call) ?? call.Receiver);
                    for(var i = 0; i < call.Arguments.Count; i++)
                    {
                        unless(Keeps(call, i), call.Arguments[i]);
                    }

                    break;
                case NewExpression creation:
                    for(var i = 0; i < creation.Arguments.Count; i++)
                    {
                        unless(Keeps(creation, i), creation.Arguments[i]);
                    }

                    break;
            }
        }

        var roots = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach(var node in nodes)
        {
            if(root(node) is { } named && (closed.Contains(node) || !safe.Contains(node)))
            {
                _ = roots.Add(named);
            }
        }

        return roots;
    }

    private bool Reads(Expression access, Property property)
    {
        return property.Getter is { IsAuto: true } || (!effects.MayWrite(access) && Origins.IsUnchangeable(access.Type));
    }

    private static int IndexOf(IReadOnlyList<Expression> arguments, Expression argument)
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(ReferenceEquals(arguments[i], argument))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static Method? Callee(InvocationExpression call)
    {
        if(call.Method is not { } selected || selected.Modifiers.IsAsync
            || TypeInitializer.TriggeredBy(call).Any(trigger => TypeInitializer.Of(trigger.Type).Initializers().Any()))
        {
            return null;
        }

        return selected.SourceDeclaration ?? selected;
    }

    private IReadOnlySet<Field>? Closure(Method start)
    {
        if(closures.TryGetValue(start, out var known))
        {
            return known;
        }

        var written = new HashSet<Field>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<Method>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Method>();
        pending.Push(start);
        while(pending.TryPop(out var method))
        {
            if(!visited.Add(method))
            {
                continue;
            }

            if(Body(method) is not var (fields, callees))
            {
                closures[start] = null;
                return null;
            }

            written.UnionWith(fields);
            foreach(var callee in callees)
            {
                pending.Push(callee);
            }
        }

        closures[start] = written;
        return written;
    }

    private (HashSet<Field> Fields, List<Method> Callees)? Body(Method method)
    {
        if(!bodies.TryGetValue(method, out var known))
        {
            known = Read(method);
            bodies[method] = known;
        }

        return known;
    }

    private (HashSet<Field> Fields, List<Method> Callees)? Read(Method method)
    {
        if(method.Body is null || method.Modifiers.IsAsync || method is { DeclaringType: AbstractType, Modifiers.IsStatic: false })
        {
            return null;
        }

        if(Kept(method) is not { } roots || roots.Contains(This))
        {
            return null;
        }

        var fields = new HashSet<Field>(ReferenceEqualityComparer.Instance);
        var callees = new List<Method>();
        var nodes = BodyWalker.DescendantsAndSelf(method.Body).ToList();
        var aliases = nodes.OfType<LocalDeclarationStatement>().Where(declaration => declaration.Local.IsRef)
            .ToDictionary<LocalDeclarationStatement, LocalVariable, Expression?>(declaration => declaration.Local, declaration => declaration.Initializer, ReferenceEqualityComparer.Instance);
        foreach(var node in nodes)
        {
            var bounded = node switch
            {
                AsyncBlockExpression or AsyncExpression => false,
                AssignmentExpression assignment => !effects.MayWrite(assignment) && Deconstructions.Pairs(assignment).All(pair => Target(pair.Target, fields, aliases)),
                CompoundAssignmentExpression compound => !effects.MayWrite(compound) && Target(compound.Target, fields, aliases),
                InvocationExpression call => Called(call, fields, callees, aliases),
                _ => !effects.MayWrite(node),
            };
            if(!bounded)
            {
                return null;
            }
        }

        return (fields, callees);
    }

    private static bool NamesThis(object node)
    {
        return node switch
        {
            ThisExpression or BackingFieldReferenceExpression => true,
            FieldReferenceExpression reference => !reference.Field.Modifiers.IsStatic,
            PropertyReferenceExpression reference => !reference.Property.Modifiers.IsStatic,
            MethodReferenceExpression reference => !reference.Method.Modifiers.IsStatic,
            FunctionReferenceExpression { Receiver: null } reference => !reference.Method.Modifiers.IsStatic,
            MethodGroupExpression { Receiver: null } group => group.Candidates.Any(candidate => !candidate.Modifiers.IsStatic),
            _ => false,
        };
    }

    private bool Called(InvocationExpression call, HashSet<Field> fields, List<Method> callees, Dictionary<LocalVariable, Expression?> aliases)
    {
        var parameters = call.Parameters;
        for(var i = 0; i < call.Arguments.Count; i++)
        {
            if(parameters is not null && i < parameters.Count && parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out
                && !Target(call.Arguments[i], fields, aliases))
            {
                return false;
            }
        }

        if(ThisTiers.ReceiverOf(call) is null or ThisExpression && Callee(call) is { } callee)
        {
            callees.Add(callee);
            return true;
        }

        return !effects.MayWrite(call);
    }

    private static bool Target(Expression target, HashSet<Field> fields, Dictionary<LocalVariable, Expression?> aliases, int depth = 0)
    {
        if(target is LocalReferenceExpression { Local: { IsRef: true } alias })
        {
            return aliases.TryGetValue(alias, out var named)
                ? named is not null && depth < aliases.Count && Target(named, fields, aliases, depth + 1)
                : alias.Parameter is not null;
        }

        var field = target switch
        {
            FieldReferenceExpression reference => reference.Field,
            MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult own } => own.Field,
            MemberAccessExpression { Member: FieldResult { Field.Modifiers.IsStatic: true } shared } => shared.Field,
            _ => null,
        };
        if(field is not null)
        {
            _ = fields.Add(field);
            return true;
        }

        return target is LocalReferenceExpression or IndexExpression or PropertyReferenceExpression or MemberAccessExpression { Member: PropertyResult };
    }
}