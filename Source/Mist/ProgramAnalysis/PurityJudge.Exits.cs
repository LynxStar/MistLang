using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal readonly record struct ExitDrop(string Name, Method Drop, SyntaxNode At, bool IsAwait, LocalDeclarationStatement? Declaration = null);

/// <summary>
/// What a scope calls on its way out without writing the call: the emitter releases every linear or
/// relevant binding the body did not hand on -- as an argument, a return value or an explicit drop --
/// through its declared Drop and then whatever that Drop hands back, its droppable fields' Drops when it
/// declares none, or an await when it is a task; and a declared Drop's own body releases every
/// droppable field it did not hand on after what it was written to do, and a lender releases what it
/// lent fresh. Except for the linear holders <see cref="LocalLifetimes"/> plans and what
/// <see cref="LentValues"/> finds, this mirrors the backend's DropInsertion rather than sharing it,
/// since analysis may not depend on a backend; a divergence between the two shows as a drop judged that
/// never runs, or one run unjudged.
/// </summary>
internal static partial class PurityJudge
{
    internal static IEnumerable<object> ScopeNodes(object start)
    {
        var pending = new Stack<object>();
        pending.Push(start);

        while(pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;

            if(node is LambdaExpression or LocalFunctionStatement or AsyncBlockExpression)
            {
                continue;
            }

            foreach(var child in BodyWalker.ChildrenOf(node).Reverse())
            {
                pending.Push(child);
            }
        }
    }

    public static IEnumerable<ExitDrop> ExitDrops(object root, object? member, IReadOnlyList<object>? nodes = null)
    {
        if(StartOf(root) is not { } start)
        {
            yield break;
        }

        nodes ??= ScopeNodes(start).ToList();
        LifetimePlan? lifetimes = null;
        var owning = root switch
        {
            LocalFunctionStatement function => function.Function,
            _ => member is Method { Body: { } body } method && ReferenceEquals(body, start) ? method : null,
        };
        var holder = owning is { Name: "Drop", DeclaringType: ConcreteType declaring } && declaring.MethodsList.Contains(owning) ? declaring : null;

        LifetimePlan planned()
        {
            return lifetimes ??= LocalLifetimes.Plan(start, owning, holder);
        }

        foreach(var declaration in nodes.OfType<LocalDeclarationStatement>())
        {
            var local = declaration.Local;

            if(!DropsAtScopeExit(local.Type))
            {
                continue;
            }

            if(LocalLifetimes.IsTracked(local.Type) && planned().Covers(declaration))
            {
                if(planned().ReleaseOf(declaration) != LifetimeRelease.None && planned().AcquiredAt(declaration) is { } acquired)
                {
                    foreach(var (drop, isAwait) in Drops(local.Type, awaitable: true, []))
                    {
                        yield return new ExitDrop(local.Name, drop, acquired, isAwait, declaration);
                    }
                }

                continue;
            }

            if(declaration.Initializer is { } initializer
                && !Moved(nodes, moved => moved is LocalReferenceExpression reference && ReferenceEquals(reference.Local, local)))
            {
                foreach(var (drop, isAwait) in Drops(local.Type, awaitable: true, []))
                {
                    yield return new ExitDrop(local.Name, drop, initializer.Node, isAwait, declaration);
                }
            }
        }

        foreach(var node in nodes)
        {
            foreach(var (value, parameter) in LentValues.Of(node))
            {
                foreach(var (drop, isAwait) in Drops(value.Type, awaitable: false, []))
                {
                    yield return new ExitDrop($"lent {parameter.Name}", drop, value.Node, isAwait);
                }
            }
        }

        if(member is Method { Name: "Drop", ParametersList.Count: 0, Modifiers.IsStatic: false, DeclaringType: ConcreteType { IsIntrinsic: false } owner, DeclarationNode: { } declared } dropping
            && ReferenceEquals(dropping.Body, root))
        {
            foreach(var field in owner.FieldsList.Where(field => !field.Modifiers.IsStatic))
            {
                if(LocalLifetimes.IsTracked(field.Type)
                    ? planned().ReleaseOf(field) == LifetimeRelease.None
                    : Moved(nodes, moved => moved switch
                    {
                        FieldReferenceExpression reference => ReferenceEquals(reference.Field, field),
                        MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult found } => ReferenceEquals(found.Field, field),
                        _ => false,
                    }))
                {
                    continue;
                }

                foreach(var (drop, _) in FieldDrops(field, owner, closing: null, []))
                {
                    yield return new ExitDrop(field.Name, drop, declared, IsAwait: false);
                }
            }
        }

        var (parameters, at) = ParametersOf(root, member);

        if(at is null)
        {
            yield break;
        }

        foreach(var (name, type, byRef, identity) in parameters)
        {
            var released = LocalLifetimes.IsTracked(type)
                ? planned().ReleaseOf(identity) != LifetimeRelease.None
                : !byRef && DropsAtScopeExit(type) && !Moved(nodes, moved => moved is LocalReferenceExpression { Local: var reached }
                    && (ReferenceEquals(reached, identity) || ReferenceEquals(reached.Parameter, identity)));

            if(released)
            {
                foreach(var (drop, isAwait) in Drops(type, awaitable: false, []))
                {
                    yield return new ExitDrop(name, drop, at, isAwait);
                }
            }
        }
    }

    private static object? StartOf(object root)
    {
        return root switch
        {
            LambdaExpression lambda => (object?)lambda.Body ?? lambda.Result,
            LocalFunctionStatement function => function.Body,
            _ => root,
        };
    }

    private static (IEnumerable<(string Name, IMistType Type, bool ByRef, object Declared)> Parameters, SyntaxNode? At) ParametersOf(object root, object? member)
    {
        return (root, member) switch
        {
            (LambdaExpression lambda, _) => (lambda.Parameters.Select(parameter => (parameter.Name, parameter.Type, parameter.IsRef, (object)parameter)), lambda.Node),
            (LocalFunctionStatement function, _) => (Declared(function.Function), function.Function.DeclarationNode),
            (_, Method method) when ReferenceEquals(method.Body, root) => (Declared(method), method.DeclarationNode),
            _ => ([], null),
        };
    }

    private static IEnumerable<(string Name, IMistType Type, bool ByRef, object Declared)> Declared(Method method)
    {
        return method.ParametersList.Select(parameter => (parameter.Name, parameter.Type, parameter.Modifier.IsRef, (object)parameter));
    }

    private static bool Moved(IReadOnlyList<object> nodes, Func<Expression, bool> isTarget)
    {
        foreach(var node in nodes)
        {
            var moved = node switch
            {
                ReturnStatement { Value: { } returned } => isTarget(returned),
                InvocationExpression invocation => MovesThroughArguments(invocation, isTarget) || IsExplicitDrop(invocation, isTarget),
                NewExpression creation => creation.Arguments.Any(isTarget),
                _ => false,
            };

            if(moved)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MovesThroughArguments(InvocationExpression invocation, Func<Expression, bool> isTarget)
    {
        var parameters = invocation.Parameters;

        for(var i = 0; i < invocation.Arguments.Count; i++)
        {
            if(isTarget(invocation.Arguments[i])
                && (parameters is null || i >= parameters.Count || !parameters[i].Modifier.IsRef))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExplicitDrop(InvocationExpression invocation, Func<Expression, bool> isTarget)
    {
        return invocation.Method is { Name: "Drop" }
            && invocation.Receiver is MemberAccessExpression { Receiver: var receiver }
            && isTarget(receiver);
    }

    private static bool DropsAtScopeExit(IMistType type)
    {
        return DefinitionOf(type) is ConcreteType { Discipline: SubstructuralDiscipline.Linear or SubstructuralDiscipline.Relevant };
    }

    public static IEnumerable<(Method Drop, bool IsAwait)> Drops(IMistType type, bool awaitable)
    {
        return Drops(type, awaitable, []);
    }

    private static IEnumerable<(Method Drop, bool IsAwait)> Drops(IMistType type, bool awaitable, HashSet<ConcreteType> visited)
    {
        if(DefinitionOf(type) is not ConcreteType concrete || !visited.Add(concrete))
        {
            yield break;
        }

        if(concrete.IsDisposableDrop)
        {
            if(Parameterless(concrete, "Dispose") is { } dispose)
            {
                yield return (dispose, false);
            }

            yield break;
        }

        var declared = Parameterless(concrete, "Drop");

        if(concrete.IsIntrinsic)
        {
            if(awaitable && declared is not null && TaskResult.IsTask(type))
            {
                yield return (declared, true);
            }

            yield break;
        }

        if(declared is not null)
        {
            yield return (declared, false);

            foreach(var drop in Drops(Closed(declared.ReturnType, concrete, type as ConstructedType), awaitable: false, visited))
            {
                yield return drop;
            }

            yield break;
        }

        if(!DropsAtScopeExit(concrete))
        {
            yield break;
        }

        foreach(var field in concrete.FieldsList.Where(field => !field.Modifiers.IsStatic))
        {
            foreach(var drop in FieldDrops(field, concrete, type as ConstructedType, visited))
            {
                yield return drop;
            }
        }
    }

    private static IEnumerable<(Method Drop, bool IsAwait)> FieldDrops(Field field, ConcreteType holder, ConstructedType? closing, HashSet<ConcreteType> visited)
    {
        var held = field.Type is TypeParameter parameter && IsDroppable(parameter) ? Closed(parameter, holder, closing) : field.Type;

        if(held is TypeParameter && field.Type is TypeParameter declared && IsDroppable(declared))
        {
            yield return (UnknownDrop, false);
            yield break;
        }

        foreach(var drop in Drops(held, awaitable: false, visited))
        {
            yield return drop;
        }
    }

    private static readonly Method UnknownDrop = new("Drop", UnknownType.Instance, [], default);

    private static bool IsDroppable(TypeParameter parameter)
    {
        return parameter.ConstraintsList.Exists(constraint => constraint is KeywordConstraint
        {
            Kind: TypeParameterConstraintKind.Linear or TypeParameterConstraintKind.Affine or TypeParameterConstraintKind.Relevant,
        });
    }

    private static IMistType Closed(IMistType declared, MemberType definition, ConstructedType? closing)
    {
        var index = declared is TypeParameter parameter ? definition.TypeParametersList.IndexOf(parameter) : -1;

        return closing is not null && index >= 0 && index < closing.TypeArguments.Count ? closing.TypeArguments[index] : declared;
    }

    private static Method? Parameterless(MemberType type, string name)
    {
        return type.MethodsList.Find(method => method.Name == name && method.ParametersList.Count == 0 && !method.Modifiers.IsStatic);
    }

    public static Method? CloneOf(IMistType type)
    {
        return DefinitionOf(type) is MemberType member ? Parameterless(member, "Clone") : null;
    }

    public static IMistType DefinitionOf(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => DefinitionOf(constructed.Definition),
            TypeAlias { AliasedType: { } aliased } => DefinitionOf(aliased),
            RefinedType refined => DefinitionOf(refined.BaseType),
            _ => type,
        };
    }
}