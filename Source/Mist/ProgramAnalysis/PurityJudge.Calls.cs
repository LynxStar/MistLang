using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal static partial class PurityJudge
{
    public static Finding? JudgeChain(object constructor, ScopeKind kind, ScopeDescription scope, Func<object, Promise> promiseOf)
    {
        var chained = constructor switch
        {
            Constructor declared => declared.ChainedConstructor ?? ImplicitlyChained(declared),
            ConcreteType type => ImplicitlyChained(type.BaseType),
            _ => null,
        };

        if(chained is null || Admits(kind, promiseOf(chained), Origin.Own))
        {
            return null;
        }

        var at = constructor switch
        {
            Constructor declared => (SyntaxNode?)declared.ChainNode ?? declared.DeclarationNode,
            ConcreteType type => type.DeclarationNode,
            _ => null,
        };

        var built = chained switch
        {
            Constructor declared => declared.DeclaringType?.Name ?? declared.Name,
            ConcreteType type => type.Name,
            _ => "?",
        };

        return at is null
            ? null
            : new Finding(
                ProgramAnalysisDiagnostics.PureScopeCallsImpure, at, [],
                [$"chain to the constructor of '{built}'", scope, promiseOf(chained).Display, Needed(kind, promiseOf(chained))]);
    }

    private static bool Admits(ScopeKind kind, Promise promise, Origin receiver)
    {
        if(promise.Pure)
        {
            return true;
        }

        if(promise is { Local: true, Readonly: true })
        {
            return receiver is Origin.Own || (receiver is Origin.Frozen && kind is ScopeKind.Pure);
        }

        return promise.Local && receiver is Origin.Own && kind is not ScopeKind.LocalReadonly;
    }

    private static string Needed(ScopeKind kind, Promise promise)
    {
        if(promise is { Local: true, Readonly: true })
        {
            return kind is ScopeKind.Pure ? "an owned or frozen receiver" : "an owned receiver";
        }

        if(promise.Local)
        {
            return kind is ScopeKind.LocalReadonly ? "a pure or local readonly callee" : "an owned receiver";
        }

        return kind is ScopeKind.LocalReadonly
            ? "a pure callee, or a local readonly one on an owned receiver"
            : "a pure callee, or a local one on an owned receiver";
    }

    /// <summary>
    /// The type whose implicit constructor an allocation naming no constructor runs, judged as a callee
    /// by its instance initializers; null for a defaultable type, whose initializers Requirement 12.1
    /// already holds pure at the type, so a second complaint at each allocation would be the same
    /// mistake twice.
    /// </summary>
    public static ConcreteType? ImplicitConstructorOf(NewExpression allocation)
    {
        return allocation.Constructor is null
            && DefinitionOf(allocation.Type) is ConcreteType { IsIntrinsic: false } type
            && !Defaultability.IsDefaultable(type)
            ? type
            : null;
    }

    private static object? ImplicitlyChained(Constructor constructor)
    {
        if(constructor.ChainKind == ConstructorChainKind.This)
        {
            return (constructor.DeclaringType as ConcreteType)?.ConstructorsList.Find(
                candidate => candidate.ParametersList.Count == 0 && !candidate.Modifiers.IsStatic && !ReferenceEquals(candidate, constructor));
        }

        return ImplicitlyChained((constructor.DeclaringType as ConcreteType)?.BaseType);
    }

    private static object? ImplicitlyChained(IMistType? baseType)
    {
        if(baseType is null || DefinitionOf(baseType) is not ConcreteType { IsIntrinsic: false } type)
        {
            return null;
        }

        var declared = type.ConstructorsList.FindAll(candidate => !candidate.Modifiers.IsStatic);

        return declared.Count == 0 ? type : declared.Find(candidate => candidate.ParametersList.Count == 0);
    }

    private sealed partial class Walk
    {
        private void JudgeInvocation(InvocationExpression invocation)
        {
            var method = invocation.Method ?? (invocation.Receiver as MethodReferenceExpression)?.Method;

            if(method is null)
            {
                JudgeFunctionValue(invocation);
                return;
            }

            var receiver = ReceiverOrigin(invocation, method);
            var name = IsAwait(invocation, method) ? "await a task" : $"call '{method.Name}'";
            var promise = JudgeCall(method, receiver, name, invocation.Node, [invocation]);
            JudgeArguments(promise, new Slot(receiver, Owned: false, ThisTiers.ReceiverOf(invocation) is null or ThisExpression), invocation.Arguments);
        }

        private void JudgeLoop(ForEachStatement loop)
        {
            var collection = loop.Collection;

            if(loop.GetEnumerator is not { } getEnumerator)
            {
                JudgeStorageRead(origins.Of(collection), $"the elements of {Spell(collection)}", collection.Node, [collection], collection);
                return;
            }

            var receiver = origins.Of(collection);

            foreach(var (callee, operation) in new (object?, string)[] { (getEnumerator, "call 'GetEnumerator'"), (loop.MoveNext, "call 'MoveNext'"), (loop.Current?.Getter, "read 'Current'") })
            {
                if(callee is null || silenced.Contains(collection))
                {
                    continue;
                }

                _ = JudgeCall(callee, receiver, $"{operation} to iterate {Spell(collection)}", collection.Node, [collection]);
            }
        }

        private void JudgeFunctionValue(InvocationExpression invocation)
        {
            if(invocation.Receiver.Type is IFunctionType { IsPure: true })
            {
                return;
            }

            if(invocation.Receiver is LocalReferenceExpression { Local: var holder } && origins.LambdaHeldBy(holder) is { } lambda)
            {
                var receiver = origins.OfCaptures(lambda);
                var promise = JudgeCall(lambda, receiver, $"call the lambda held by '{holder.Name}'", invocation.Node, [invocation]);
                JudgeArguments(promise, new Slot(receiver, Owned: false, OfThis: false), invocation.Arguments);
                return;
            }

            Refuse(
                ProgramAnalysisDiagnostics.PureScopeCallsImpure, invocation.Node, [invocation],
                "call a function value that is not pure", scope, "of a function type without 'pure'",
                "a pure function type, whose values are proven pure where they convert");
        }

        private void JudgeClone(CloneExpression clone)
        {
            var source = origins.Of(clone.Source);

            if(CloneOf(clone.Type) is { } method)
            {
                _ = JudgeCall(method, source, $"call 'Clone' on {Spell(clone.Source)}", clone.Node, [clone]);
            }

            if(!silenced.Contains(clone))
            {
                JudgeStorageRead(source, $"a clone of {Spell(clone.Source)}", clone.Node, [clone], clone.Source);
            }
        }

        private void JudgeAllocation(NewExpression allocation)
        {
            var constructor = allocation.Constructor ?? (object?)ImplicitConstructorOf(allocation);

            if(constructor is null)
            {
                return;
            }

            var promise = promiseOf(constructor);

            if(!promise.Pure)
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, allocation.Node, [allocation],
                    $"call the constructor of '{TypeDisplay.Mist(allocation.Type)}'", scope, promise.Display, "a pure constructor");
            }
        }

        private Promise JudgeCall(object callee, Origin receiver, string operation, SyntaxNode at, IReadOnlyList<Expression> about)
        {
            var promise = promiseOf(callee);

            if(!Admits(kind, promise, receiver))
            {
                Refuse(ProgramAnalysisDiagnostics.PureScopeCallsImpure, at, about, operation, scope, promise.Display, Needed(kind, promise));
            }

            return promise;
        }

        private void JudgeArguments(Promise promise, Slot receiver, IReadOnlyList<Expression> arguments)
        {
            if(promise is not { Local: true, Readonly: false, Pure: false } || receiver.Holder is not Origin.Own)
            {
                return;
            }

            foreach(var argument in arguments)
            {
                JudgeStore(argument, receiver);
            }
        }

        private Origin ReceiverOrigin(InvocationExpression invocation, Method method)
        {
            if(method.Modifiers.IsStatic)
            {
                return Origin.External;
            }

            if(method.DeclaringType is null && origins.FunctionOf(method) is { } function)
            {
                return origins.OfCaptures(function);
            }

            return ThisTiers.ReceiverOf(invocation) is { } receiver ? origins.Of(receiver) : origins.This;
        }

        private static bool IsAwait(InvocationExpression invocation, Method method)
        {
            return method.Name == "Drop"
                && invocation.Arguments.Count == 0
                && ThisTiers.ReceiverOf(invocation) is { } task
                && TaskResult.IsTask(task.Type);
        }
    }
}