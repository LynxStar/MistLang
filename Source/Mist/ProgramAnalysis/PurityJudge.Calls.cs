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
                [new NameValue($"chain to the constructor of '{built}'"), new NameValue(scope.Text), new EnumValue(promiseOf(chained).Display), new EnumValue(Needed(kind, promiseOf(chained)))])
            : new Finding(
                ProgramAnalysisDiagnostics.PureScopeReadsMutable, at, [],
                [new NameValue($"the ambient '{undeclared}' through the constructor of '{built}'"), new NameValue(scope.Text),
                    new NameValue($"it is an input the scope does not declare, so it can differ between calls; 'with {undeclared}' on the scope would make it one")]);
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
            ConcreteType type => Defaultability.IsDefaultable(type) && type.BaseType is { } baseType && Defaultability.DeclarationOf(baseType) is { } defaulted
                ? defaulted
                : ImplicitChains.Of(type),
            _ => null,
        };
    }

    public static ConcreteType? ImplicitConstructorOf(NewExpression allocation)
    {
        return allocation.Constructor is null && DefinitionOf(allocation.Type) is ConcreteType { IsIntrinsic: false } type ? type : null;
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
        var declaring = constructor.DeclaringType as ConcreteType;

        if(constructor.ChainKind != ConstructorChainKind.This)
        {
            return ImplicitChains.Of(declaring);
        }

        return declaring is null
            ? null
            : ImplicitChains.CalledWithNothing(
                declaring.ConstructorsList.FindAll(candidate => !candidate.Modifiers.IsStatic && !ReferenceEquals(candidate, constructor)),
                declaring);
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
                    new NameValue(operation), new NameValue(scope.Text), new EnumValue("comparing values through an equality or ordering whose code the program cannot see"),
                    new EnumValue("values whose comparison is intrinsic or declared in Mist"));
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
                new NameValue($"call '{method.Name}'"), new NameValue(scope.Text), new EnumValue($"{promiseOf(method).Display} only while its receiver is frozen, and the receiver is {Describe(receiver)}"),
                new EnumValue(kind is ScopeKind.Pure ? "an owned or frozen receiver" : "an owned receiver"));
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
                    && ParamsCollection.IsHandedWhole(Unwrapped(argument).Type);

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
                new NameValue($"the ambient '{context}' through {through}"), new NameValue(scope.Text),
                new NameValue($"it is an input the scope does not declare, so it can differ between calls; 'with {context}' on the scope would make it one"));
        }

        private void JudgeLoop(ForEachStatement loop)
        {
            var collection = loop.Collection;

            if(ClrCallbacks.IsUnrecorded(loop))
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, collection.Node, [collection],
                    new NameValue($"iterate {Spell(collection)}"), new NameValue(scope.Text), new EnumValue("run through a GetEnumerator only emission resolves, an extension's or the runtime's"),
                    new EnumValue("a collection whose own GetEnumerator states its promise"));
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
                new NameValue("call a function value that is not pure"), new NameValue(scope.Text), new EnumValue("of a function type without 'pure'"),
                new EnumValue("a pure function type, whose values are proven pure where they convert"));
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
                    new NameValue($"take the default of '{TypeDisplay.Mist(fallback.Type)}'"), new NameValue(scope.Text),
                    new EnumValue("chosen by the type argument, and a default constraint states no promise"), new EnumValue("a default of a known type"));
            }
        }

        private void JudgeAllocation(NewExpression allocation)
        {
            if(AllocatesTypeParameter(allocation))
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, allocation.Node, [allocation],
                    new NameValue($"call the constructor of '{TypeDisplay.Mist(allocation.Type)}'"), new NameValue(scope.Text),
                    new EnumValue("chosen by the type argument, and a constructor constraint states no promise"), new EnumValue("a pure constructor"));
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
                    new NameValue($"call the constructor of '{TypeDisplay.Mist(allocation.Type)}'"), new NameValue(scope.Text), new EnumValue(promise.Display), new EnumValue("a pure constructor"));
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
                Refuse(ProgramAnalysisDiagnostics.PureScopeCallsImpure, at, about, new NameValue(operation), new NameValue(scope.Text), new EnumValue(promise.Display), new EnumValue(Needed(kind, promise)));
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