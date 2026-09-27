using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
        var chained = ChainedOf(constructor);
        var undeclared = chained is Constructor called
            ? UndeclaredContext((constructor as Constructor)?.ContextBindingsList ?? [], called.ContextBindingsList)
            : null;

        if(chained is null || (Admits(kind, promiseOf(chained), Origin.Own) && undeclared is null))
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

        if(at is null)
        {
            return null;
        }

        return undeclared is null || !Admits(kind, promiseOf(chained), Origin.Own)
            ? new Finding(
                ProgramAnalysisDiagnostics.PureScopeCallsImpure, at, [],
                [$"chain to the constructor of '{built}'", scope, promiseOf(chained).Display, Needed(kind, promiseOf(chained))])
            : new Finding(
                ProgramAnalysisDiagnostics.PureScopeReadsMutable, at, [],
                [$"the ambient '{undeclared}' through the constructor of '{built}'", scope,
                    $"it is an input the scope does not declare, so it can differ between calls; 'with {undeclared}' on the scope would make it one"]);
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

    private static string? UndeclaredContext(IReadOnlyList<ContextBinding> declared, IReadOnlyList<ContextBinding> required)
    {
        return required.FirstOrDefault(needed => !declared.Any(binding => SameType(binding.ContextType, needed.ContextType))) is { } missing
            ? TypeDisplay.Mist(missing.ContextType)
            : null;
    }

    private static bool SameType(IMistType left, IMistType right)
    {
        return ReferenceEquals(left, right) || string.Equals(left.FullyQualifiedName, right.FullyQualifiedName, StringComparison.Ordinal);
    }

    public static object? ChainedOf(object constructor)
    {
        return constructor switch
        {
            Constructor declared => declared.ChainedConstructor ?? ImplicitlyChained(declared),
            ConcreteType type => ImplicitlyChained(type.BaseType),
            _ => null,
        };
    }

    public static ConcreteType? ImplicitConstructorOf(NewExpression allocation)
    {
        return allocation.Constructor is null && DefinitionOf(allocation.Type) is ConcreteType { IsIntrinsic: false } type ? type : null;
    }

    public static object? DefaultChainOf(DefaultExpression fallback)
    {
        return DefinitionOf(fallback.Type) is ClassType { IsIntrinsic: false } type && Defaultability.IsDefaultable(type)
            ? ImplicitlyChained(type.BaseType)
            : null;
    }

    public static bool AllocatesTypeParameter(NewExpression allocation)
    {
        return allocation.Constructor is null && DefinitionOf(allocation.Type) is TypeParameter;
    }

    public static bool DefaultsTypeParameter(DefaultExpression fallback)
    {
        return DefinitionOf(fallback.Type) is TypeParameter parameter
            && parameter.ConstraintsList.Exists(constraint => constraint is KeywordConstraint { Kind: TypeParameterConstraintKind.Default });
    }

    private static object? ImplicitlyChained(Constructor constructor)
    {
        if(constructor.ChainKind == ConstructorChainKind.This)
        {
            return CalledWithNothing((constructor.DeclaringType as ConcreteType)?.ConstructorsList.FindAll(
                candidate => !candidate.Modifiers.IsStatic && !ReferenceEquals(candidate, constructor)) ?? []);
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

        return declared.Count == 0 ? type : CalledWithNothing(declared);
    }

    private static Constructor? CalledWithNothing(List<Constructor> candidates)
    {
        return candidates.Find(candidate => candidate.ParametersList.Count == 0)
            ?? candidates.Find(candidate => candidate.ParametersList is [{ IsFlattened: true }]);
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
            JudgeFrozenReceiver(invocation, method, receiver);
            JudgeComparisons(ElementComparisons.Of(invocation, out var element), $"call '{method.Name}' on a list of '{TypeDisplay.Mist(element ?? UnknownType.Instance)}'", invocation);
            JudgeArguments(promise, new Slot(receiver, Owned: false, ThisTiers.ReceiverOf(invocation) is null or ThisExpression), invocation.Arguments);
            JudgeLends(invocation.Arguments, method.ParametersList, method.Name);
            JudgeContexts(method.ContextBindingsList, $"'{method.Name}'", invocation);
        }

        private void JudgeComparisons(IReadOnlyList<Method>? compared, string operation, Expression at)
        {
            if(silenced.Contains(at))
            {
                return;
            }

            if(compared is null)
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, at.Node, [at],
                    operation, scope, "comparing values through an equality or ordering whose code the program cannot see",
                    "values whose comparison is intrinsic or declared in Mist");
                return;
            }

            foreach(var callee in compared)
            {
                if(!silenced.Contains(at))
                {
                    _ = JudgeCall(callee, Origin.External, $"{operation}, whose comparison calls '{callee.Name}',", at.Node, [at]);
                }
            }
        }

        private MistType? ScopeType()
        {
            return origins.Member switch
            {
                Method method => method.DeclaringType,
                Property property => property.DeclaringType,
                Indexer indexer => indexer.DeclaringType,
                TypeInitializer initializer => initializer.Type,
                ConcreteType type => type,
                _ => null,
            };
        }

        private void JudgeFrozenReceiver(InvocationExpression invocation, Method method, Origin receiver)
        {
            var through = ThisTiers.ReceiverOf(invocation);

            if(method is not { DeclaredThisTier: ThisTier.Frozen, Modifiers.IsStatic: false, DeclaringType: { } declaring }
                || silenced.Contains(invocation)
                || receiver is Origin.Own || (receiver is Origin.Frozen && kind is ScopeKind.Pure)
                || ThisTiers.IsValue(through?.Type ?? declaring) || Origins.IsUnchangeable(through?.Type ?? declaring)
                || TierAnalyzer.RefusesReceiver(through, ThisTier.Frozen))
            {
                return;
            }

            Refuse(
                ProgramAnalysisDiagnostics.PureScopeCallsImpure, invocation.Node, [invocation],
                $"call '{method.Name}'", scope, $"{promiseOf(method).Display} only while its receiver is frozen, and the receiver is {Describe(receiver)}",
                kind is ScopeKind.Pure ? "an owned or frozen receiver" : "an owned receiver");
        }

        /// <summary>
        /// A callee reads a frozen parameter, and the array a params parameter is handed whole, as a loan
        /// it holds frozen; so the argument must be one the calling scope could read itself. A local the
        /// ownership analyzer already refuses for the lend is left to it.
        /// </summary>
        private void JudgeLends(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter> parameters, string callee)
        {
            var flattened = ValuePack.FlattenedSlot(parameters);

            for(var i = 0; i < arguments.Count && i < parameters.Count; i++)
            {
                var argument = arguments[i];
                var lent = parameters[i].Modifier.Ownership == OwnershipKind.Frozen && !TierAnalyzer.RefusesLend(argument, ThisTier.Frozen);
                var whole = i == flattened && arguments.Count == parameters.Count && parameters[i].Type is not ITupleType
                    && Indexability.ArrayElement(Unwrapped(argument).Type) is not null;

                if((!lent && !whole) || silenced.Contains(argument) || ThisTiers.IsValue(argument.Type) || Origins.IsUnchangeable(argument.Type))
                {
                    continue;
                }

                JudgeStorageRead(
                    origins.Of(argument), $"{Spell(argument)}, lent to '{callee}' as '{parameters[i].Name}', which reads it as frozen,",
                    argument.Node, [argument], argument);
            }
        }

        private void JudgeContexts(IReadOnlyList<ContextBinding> required, string through, Expression at)
        {
            if(silenced.Contains(at) || UndeclaredContext((origins.Member as Method)?.ContextBindingsList ?? [], required) is not { } context)
            {
                return;
            }

            Refuse(
                ProgramAnalysisDiagnostics.PureScopeReadsMutable, at.Node, [at],
                $"the ambient '{context}' through {through}", scope,
                $"it is an input the scope does not declare, so it can differ between calls; 'with {context}' on the scope would make it one");
        }

        private void JudgeLoop(ForEachStatement loop)
        {
            var collection = loop.Collection;

            if(ClrCallbacks.IsUnrecorded(loop))
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, collection.Node, [collection],
                    $"iterate {Spell(collection)}", scope, "run through a GetEnumerator only emission resolves, an extension's or the runtime's",
                    "a collection whose own GetEnumerator states its promise");
                return;
            }

            if(loop.GetEnumerator is not { } getEnumerator)
            {
                JudgeStorageRead(origins.Of(collection), $"the elements of {Spell(collection)}", collection.Node, [collection], collection);
                return;
            }

            var receiver = origins.Of(collection);
            var dispose = ClrCallbacks.DisposeOf(loop);

            foreach(var (callee, operation) in new (object?, string)[] { (getEnumerator, "call 'GetEnumerator'"), (loop.MoveNext, "call 'MoveNext'"), (loop.Current?.Getter, "read 'Current'"), (dispose, "call 'Dispose'") })
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

        private void JudgeDefault(DefaultExpression fallback)
        {
            if(DefaultsTypeParameter(fallback))
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, fallback.Node, [fallback],
                    $"take the default of '{TypeDisplay.Mist(fallback.Type)}'", scope,
                    "chosen by the type argument, and a default constraint states no promise", "a default of a known type");
                return;
            }

            if(DefaultChainOf(fallback) is { } chained && promiseOf(chained) is { Pure: false } promise)
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, fallback.Node, [fallback],
                    $"take the default of '{TypeDisplay.Mist(fallback.Type)}', which chains to the constructor of its base,", scope, promise.Display, "a pure base constructor");
            }
        }

        private void JudgeAllocation(NewExpression allocation)
        {
            if(AllocatesTypeParameter(allocation))
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, allocation.Node, [allocation],
                    $"call the constructor of '{TypeDisplay.Mist(allocation.Type)}'", scope,
                    "chosen by the type argument, and a constructor constraint states no promise", "a pure constructor");
                return;
            }

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

            if(allocation.Constructor is { } declared)
            {
                JudgeLends(allocation.Arguments, declared.ParametersList, declared.DeclaringType?.Name ?? declared.Name);
                JudgeContexts(declared.ContextBindingsList, $"the constructor of '{TypeDisplay.Mist(allocation.Type)}'", allocation);
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