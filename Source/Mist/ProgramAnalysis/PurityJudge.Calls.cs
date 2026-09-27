using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal static partial class PurityJudge
{
    public static Finding? JudgeChain(Constructor constructor, ScopeDescription scope, Func<object, Promise> promiseOf)
    {
        if((constructor.ChainedConstructor ?? ImplicitlyChained(constructor)) is not { } chained || promiseOf(chained).Pure)
        {
            return null;
        }

        var at = (SyntaxNode?)constructor.ChainNode ?? constructor.DeclarationNode;

        return at is null
            ? null
            : new Finding(
                ProgramAnalysisDiagnostics.PureScopeCallsImpure, at, [],
                [$"chain to the constructor of '{chained.DeclaringType?.Name ?? chained.Name}'", scope, promiseOf(chained).Display, "a pure constructor"]);
    }

    private static Constructor? ImplicitlyChained(Constructor constructor)
    {
        var target = constructor.ChainKind == ConstructorChainKind.This
            ? constructor.DeclaringType
            : (constructor.DeclaringType as ConcreteType)?.BaseType;

        return (target as ConcreteType)?.ConstructorsList.Find(
            candidate => candidate.ParametersList.Count == 0 && !candidate.Modifiers.IsStatic && !ReferenceEquals(candidate, constructor));
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
            JudgeArguments(promise, receiver, invocation.Arguments);
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
                JudgeArguments(promise, receiver, invocation.Arguments);
                return;
            }

            Refuse(
                ProgramAnalysisDiagnostics.PureScopeCallsImpure, invocation.Node, [invocation],
                "call a function value that is not pure", scope, "of a function type without 'pure'",
                "a pure function type, whose values are proven pure where they convert");
        }

        private void JudgeAllocation(NewExpression allocation)
        {
            if(allocation.Constructor is not { } constructor)
            {
                return;
            }

            _ = JudgeCall(
                constructor, Origin.Own, $"call the constructor of '{TypeDisplay.Mist(allocation.Type)}'",
                allocation.Node, [allocation], needed: "a pure constructor");
        }

        private Promise JudgeCall(object callee, Origin receiver, string operation, SyntaxNode at, IReadOnlyList<Expression> about, string? needed = null)
        {
            var promise = promiseOf(callee);

            if(!Admits(promise, receiver))
            {
                Refuse(ProgramAnalysisDiagnostics.PureScopeCallsImpure, at, about, operation, scope, promise.Display, needed ?? Needed(promise));
            }

            return promise;
        }

        private bool Admits(Promise promise, Origin receiver)
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

        private string Needed(Promise promise)
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

        private void JudgeArguments(Promise promise, Origin receiver, IReadOnlyList<Expression> arguments)
        {
            if(promise is not { Local: true, Readonly: false, Pure: false } || receiver is not Origin.Own)
            {
                return;
            }

            foreach(var argument in arguments)
            {
                JudgeStore(argument);
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