using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal readonly record struct ExitDrop(string Name, Method Drop, SyntaxNode At, bool IsAwait);

/// <summary>
/// What a scope calls on its way out without writing the call: the emitter releases every linear or
/// relevant binding the body did not hand on -- as an argument, a return value or an explicit drop --
/// through its declared Drop, its droppable fields' Drops when it declares none, or an await when it is
/// a task. Mirrors the backend's DropInsertion rather than sharing it, since analysis may not depend on
/// a backend; a divergence between the two shows as a drop judged that never runs, or one run unjudged.
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

        foreach(var declaration in nodes.OfType<LocalDeclarationStatement>())
        {
            var local = declaration.Local;

            if(declaration.Initializer is { } initializer
                && DropsAtScopeExit(local.Type)
                && !Moved(nodes, reference => ReferenceEquals(reference.Local, local)))
            {
                foreach(var (drop, isAwait) in Drops(local.Type, awaitable: true, []))
                {
                    yield return new ExitDrop(local.Name, drop, initializer.Node, isAwait);
                }
            }
        }

        var (parameters, at) = ParametersOf(root, member);

        if(at is null)
        {
            yield break;
        }

        foreach(var (name, type, byRef) in parameters)
        {
            if(!byRef && DropsAtScopeExit(type) && !Moved(nodes, reference => reference.Local.Name == name))
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

    private static (IEnumerable<(string Name, IMistType Type, bool ByRef)> Parameters, SyntaxNode? At) ParametersOf(object root, object? member)
    {
        return (root, member) switch
        {
            (LambdaExpression lambda, _) => (lambda.Parameters.Select(parameter => (parameter.Name, parameter.Type, parameter.IsRef)), lambda.Node),
            (LocalFunctionStatement function, _) => (Declared(function.Function), function.Function.DeclarationNode),
            (_, Method method) when ReferenceEquals(method.Body, root) => (Declared(method), method.DeclarationNode),
            _ => ([], null),
        };
    }

    private static IEnumerable<(string Name, IMistType Type, bool ByRef)> Declared(Method method)
    {
        return method.ParametersList.Select(parameter => (parameter.Name, parameter.Type, parameter.Modifier.IsRef));
    }

    private static bool Moved(IReadOnlyList<object> nodes, Func<LocalReferenceExpression, bool> isTarget)
    {
        foreach(var node in nodes)
        {
            var moved = node switch
            {
                ReturnStatement { Value: LocalReferenceExpression returned } => isTarget(returned),
                InvocationExpression invocation => MovesThroughArguments(invocation, isTarget) || IsExplicitDrop(invocation, isTarget),
                NewExpression creation => creation.Arguments.Any(argument => argument is LocalReferenceExpression reference && isTarget(reference)),
                _ => false,
            };

            if(moved)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MovesThroughArguments(InvocationExpression invocation, Func<LocalReferenceExpression, bool> isTarget)
    {
        var parameters = invocation.Method?.ParametersList;

        for(var i = 0; i < invocation.Arguments.Count; i++)
        {
            if(invocation.Arguments[i] is LocalReferenceExpression reference
                && isTarget(reference)
                && (parameters is null || i >= parameters.Count || !parameters[i].Modifier.IsRef))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExplicitDrop(InvocationExpression invocation, Func<LocalReferenceExpression, bool> isTarget)
    {
        return invocation.Method is { Name: "Drop" }
            && invocation.Receiver is MemberAccessExpression { Receiver: LocalReferenceExpression reference }
            && isTarget(reference);
    }

    private static bool DropsAtScopeExit(IMistType type)
    {
        return DefinitionOf(type) is ConcreteType { Discipline: SubstructuralDiscipline.Linear or SubstructuralDiscipline.Relevant };
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
            yield break;
        }

        foreach(var field in concrete.FieldsList.Where(field => !field.Modifiers.IsStatic))
        {
            foreach(var drop in Drops(field.Type, awaitable: false, visited))
            {
                yield return drop;
            }
        }
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