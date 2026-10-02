using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The declared promises, judged: every body whose member says pure, local or local readonly, every
/// lambda that is pure, local or of a pure function type, every local function that declares one, every
/// settled method group of pure function type, and -- purity Requirement 12.1 -- every field initializer
/// and default entry of a defaultable type, as a pure scope with no inputs. Those initializers are
/// judged at the type alone, never again as roots of a constructor or an allocation that runs them,
/// where a second complaint would be the same mistake twice. An initializer that IS a lambda is a
/// closure of the initializer's scope, judged where it converts like any other. An inferred promise is
/// never reported (Requirement 3): a member that declares nothing is consulted only as a callee,
/// through <see cref="PurityInference"/>.
/// </summary>
internal sealed class PurityAnalyzer : IProgramAnalyzer
{
    public string Id => "purity-checking";

    public void Analyze(AnalysisContext context)
    {
        new Walk(context).Run();
    }

    private sealed class Walk(AnalysisContext context)
    {
        private readonly PurityInference inference = new(context);
        private readonly Dictionary<(object Scope, ScopeKind Kind), Origins> origins = [];
        private readonly Dictionary<object, object> enclosingOf = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> closures = [];

        public void Run()
        {
            foreach(var root in context.Bodies)
            {
                Collect(root.Root, root.Root);

                if(root.Root is LambdaExpression lambda)
                {
                    enclosingOf[lambda] = root;
                    closures.Add(lambda);
                }

                if(KindOf(root) is { } kind)
                {
                    JudgeRoot(root, kind);
                }
            }

            foreach(var closure in closures)
            {
                JudgeClosure(closure);
            }

            foreach(var reference in context.Nodes.OfType<FunctionReferenceExpression>())
            {
                if(reference.Type is IFunctionType { IsPure: true })
                {
                    JudgeReference(reference);
                }
            }

            foreach(var type in context.Types)
            {
                if(type is ConcreteType concrete && Defaultability.IsDefaultable(concrete))
                {
                    JudgeDefaults(concrete);
                }
                else if(type is ConcreteType { Modifiers.IsPure: true } promised && !promised.ConstructorsList.Exists(constructor => !constructor.Modifiers.IsStatic))
                {
                    JudgeImplicitConstructor(promised);
                }

                if(type is MemberType member)
                {
                    JudgeAutoSetters(member);
                }
            }
        }

        private void JudgeImplicitConstructor(ConcreteType type)
        {
            var scope = ScopeDescription.Of(ScopeKind.Pure, type);

            foreach(var initializer in PurityInference.ImplicitConstructorInitializers(type))
            {
                Report(PurityJudge.Judge(initializer, ScopeKind.Pure, scope, new Origins(initializer, type, ScopeKind.Pure), inference.Of));
            }

            if(PurityJudge.JudgeChain(type, ScopeKind.Pure, scope, inference.Of) is { } chain)
            {
                Report([chain]);
            }
        }

        private void JudgeAutoSetters(MemberType type)
        {
            foreach(var property in type.PropertiesList)
            {
                if(property is { Getter: { IsAuto: true } getter, Setter: { IsInit: false } written, DeclarationNode: { } read }
                    && KindOf(getter.OwnModifiers ?? property.Modifiers) is ScopeKind.Pure
                    && KindOf(written.OwnModifiers ?? property.Modifiers) is not (ScopeKind.Pure or ScopeKind.LocalReadonly))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.PureScopeReadsMutable, read, [],
                        $"the storage of '{property.Name}'", ScopeDescription.Of(ScopeKind.Pure, property),
                        "its setter can change it between reads, so an auto getter over it keeps local readonly at most");
                }

                if(property is not { Setter: { IsAuto: true, IsInit: false } setter, DeclarationNode: { } at }
                    || KindOf(setter.OwnModifiers ?? property.Modifiers) is not ScopeKind.Pure and not ScopeKind.LocalReadonly)
                {
                    continue;
                }

                var kind = KindOf(setter.OwnModifiers ?? property.Modifiers)!.Value;

                context.Report(
                    ProgramAnalysisDiagnostics.PureScopeWrites, at, [],
                    $"the storage of '{property.Name}'", ScopeDescription.Of(kind, property),
                    $"an auto setter does nothing but write it, and a {ScopeDescription.Spell(kind)} setter writes nothing outside what it owns");
            }
        }

        private void Collect(object node, object scope)
        {
            foreach(var child in BodyWalker.ChildrenOf(node))
            {
                switch(child)
                {
                    case LambdaExpression lambda:
                        enclosingOf[lambda] = scope;
                        closures.Add(lambda);
                        Collect(lambda, lambda);
                        break;

                    case LocalFunctionStatement function:
                        enclosingOf[function] = scope;
                        closures.Add(function);
                        Collect(function, function);
                        break;

                    default:
                        Collect(child, scope);
                        break;
                }
            }
        }

        private static ScopeKind? KindOf(SourceRoot root)
        {
            return root.Member switch
            {
                Method method => KindOf(method.Modifiers),
                Property property when PurityInference.AccessorOf(property.Getter, property.Setter, root.Root) is { } accessor =>
                    KindOf(accessor.OwnModifiers ?? property.Modifiers),
                Indexer indexer when PurityInference.AccessorOf(indexer.Getter, indexer.Setter, root.Root) is { } accessor =>
                    KindOf(accessor.OwnModifiers ?? indexer.Modifiers),
                _ => null,
            };
        }

        private static ScopeKind? KindOf(MemberModifiers modifiers)
        {
            return modifiers switch
            {
                { IsPure: true } => ScopeKind.Pure,
                { IsLocal: true, IsReadonly: true } => ScopeKind.LocalReadonly,
                { IsLocal: true } => ScopeKind.Local,
                _ => null,
            };
        }

        private void JudgeRoot(SourceRoot root, ScopeKind kind)
        {
            var scope = ScopeDescription.Of(kind, root.Member);
            Report(PurityJudge.Judge(root.Root, kind, scope, OriginsFor(root.Root, kind), inference.Of));

            if(root.Member is not Constructor constructor || !ReferenceEquals(constructor.Body, root.Root))
            {
                return;
            }

            if(constructor.DeclaringType is ConcreteType type && !Defaultability.IsDefaultable(type))
            {
                foreach(var initializer in PurityInference.InstanceInitializers(constructor))
                {
                    Report(PurityJudge.Judge(initializer, kind, scope, new Origins(initializer, constructor, kind), inference.Of));
                }
            }

            if(PurityJudge.JudgeChain(constructor, kind, scope, inference.Of) is { } chain)
            {
                Report([chain]);
            }
        }

        private void JudgeClosure(object closure)
        {
            switch(closure)
            {
                case LambdaExpression lambda when LambdaKind(lambda) is { } kind:
                    Report(PurityJudge.Judge(lambda, kind, ScopeDescription.Lambda(kind), OriginsFor(lambda, kind), inference.Of));
                    break;

                case LocalFunctionStatement function when KindOf(function.Function.Modifiers) is { } kind:
                    Report(PurityJudge.Judge(
                        function, kind, ScopeDescription.LocalFunction(kind, function.Function.Name), OriginsFor(function, kind), inference.Of));
                    break;
            }
        }

        private static ScopeKind? LambdaKind(LambdaExpression lambda)
        {
            if(lambda.IsPure || lambda.Type is IFunctionType { IsPure: true })
            {
                return ScopeKind.Pure;
            }

            return lambda.IsLocal ? ScopeKind.Local : null;
        }

        private void JudgeReference(FunctionReferenceExpression reference)
        {
            var promise = inference.Of(reference.Method);

            if(!promise.Pure)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.PureFunctionRequired, reference.Node, [reference],
                    reference.Method.Name, TypeDisplay.Mist(reference.Type), promise.Display);
            }
            else if(reference.Method.DeclaredThisTier == ThisTier.Frozen && reference.Receiver is { } receiver && !Origins.IsUnchangeable(receiver.Type))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.PureFunctionRequired, reference.Node, [reference],
                    reference.Method.Name, TypeDisplay.Mist(reference.Type), "pure only while its receiver is frozen, a loan the function value outlives");
            }
            else if(reference.Method.ParametersList.Find(parameter => parameter.Modifier.Ownership is OwnershipKind.Frozen or OwnershipKind.Owned) is { } lent)
            {
                var tier = lent.Modifier.Ownership == OwnershipKind.Frozen ? "lent frozen" : "moved in";

                context.Report(
                    ProgramAnalysisDiagnostics.PureFunctionRequired, reference.Node, [reference],
                    reference.Method.Name, TypeDisplay.Mist(reference.Type),
                    $"pure only while its argument for '{lent.Name}' is {tier}, which a call through the function value never does");
            }
            else if(reference.Method.ContextBindingsList is [var ambient, ..])
            {
                context.Report(
                    ProgramAnalysisDiagnostics.PureFunctionRequired, reference.Node, [reference],
                    reference.Method.Name, TypeDisplay.Mist(reference.Type),
                    $"pure only given the ambient '{TypeDisplay.Mist(ambient.ContextType)}' it reads, an input a call through the function value does not supply");
            }
            else if(reference.Method is { Modifiers.IsStatic: true, DeclaringType: ConcreteType declaring }
                && TypeInitializer.Of(declaring) is { IsPrecise: true } initializer && !inference.Of(initializer).Pure)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.PureFunctionRequired, reference.Node, [reference],
                    reference.Method.Name, TypeDisplay.Mist(reference.Type),
                    $"pure only once the static initializers of '{declaring.Name}' have run, and a call through the function value can be the first use of the type");
            }
        }

        private void JudgeDefaults(ConcreteType type)
        {
            foreach(var slot in InstanceStorage.Of(type))
            {
                if(slot.Initializer is { } initializer)
                {
                    JudgeDefault(initializer, slot.Member, ScopeDescription.FieldInitializer(slot.Name, type.Name), type);
                }
            }

            foreach(var entry in type.DefaultBlock?.Entries ?? [])
            {
                JudgeEntry(entry, type.Name, type);
            }
        }

        private void JudgeEntry(DefaultEntry entry, string boundTypeName, ConcreteType type)
        {
            switch(entry)
            {
                case DefaultValueEntry valued:
                    JudgeDefault(valued.Value, member: null, ScopeDescription.DefaultEntry(entry.FieldName, boundTypeName), type);
                    break;

                case DefaultBindingEntry binding:
                    var nestedName = binding.BoundType is { } bound ? TypeDisplay.Mist(bound) : boundTypeName;

                    foreach(var nested in binding.NestedEntries)
                    {
                        JudgeEntry(nested, nestedName, type);
                    }

                    break;
            }
        }

        private void JudgeDefault(Expression root, object? member, ScopeDescription scope, ConcreteType type)
        {
            var noInputs = new Origins(root, member, ScopeKind.Pure);

            foreach(var finding in PurityJudge.Judge(root, ScopeKind.Pure, scope, noInputs, inference.Of))
            {
                var reason = string.Format(CultureInfo.InvariantCulture, finding.Descriptor.MessageTemplate, finding.Args);

                context.Report(
                    ProgramAnalysisDiagnostics.DefaultNotConsistent, finding.At, finding.About,
                    char.ToUpperInvariant(scope.Text[0]) + scope.Text[1..], type.Name, reason);
            }
        }

        private Origins OriginsFor(object scope, ScopeKind kind)
        {
            if(origins.TryGetValue((scope, kind), out var known))
            {
                return known;
            }

            var made = scope switch
            {
                LambdaExpression lambda => new Origins(lambda, lambda, kind, OriginsFor(enclosingOf[lambda], kind), capturesAreInputs: kind is ScopeKind.Pure),
                LocalFunctionStatement function => new Origins(function, function.Function, kind, OriginsFor(enclosingOf[function], kind), capturesAreInputs: kind is ScopeKind.Pure),
                SourceRoot source => new Origins(source.Root, source.Member, kind),
                _ => new Origins(scope, context.MemberOf(scope), kind),
            };

            origins[(scope, kind)] = made;
            return made;
        }

        private void Report(IEnumerable<Finding> findings)
        {
            foreach(var finding in findings)
            {
                context.Report(finding.Descriptor, finding.At, finding.About, finding.Args);
            }
        }
    }
}