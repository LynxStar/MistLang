using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The declared promises, judged: every body whose member says pure, local or local readonly, every
/// lambda that is pure, local or of a pure function type, every local function that declares one, every
/// settled method group of pure function type, and -- purity Requirement 12.1 -- every field initializer
/// and default entry of a defaultable type, as a pure scope with no inputs. An inferred promise is never
/// reported (Requirement 3): a member that declares nothing is consulted only as a callee, through
/// <see cref="PurityInference"/>. Order 13 of the analysis register.
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
                Property property when IsAccessorBody(property.Getter, property.Setter, root.Root) => KindOf(property.Modifiers),
                Indexer indexer when IsAccessorBody(indexer.Getter, indexer.Setter, root.Root) => KindOf(indexer.Modifiers),
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

        private static bool IsAccessorBody(PropertyAccessor? getter, PropertyAccessor? setter, object root)
        {
            return IsBodyOf(getter, root) || IsBodyOf(setter, root);
        }

        private static bool IsBodyOf(PropertyAccessor? accessor, object root)
        {
            return accessor is not null && (ReferenceEquals(accessor.Body, root) || ReferenceEquals(accessor.ExpressionBody, root));
        }

        private void JudgeRoot(SourceRoot root, ScopeKind kind)
        {
            var scope = ScopeDescription.Of(kind, root.Member);
            Report(PurityJudge.Judge(root.Root, kind, scope, OriginsFor(root.Root, kind), inference.Of));

            if(kind is ScopeKind.Pure
                && root.Member is Constructor constructor
                && ReferenceEquals(constructor.Body, root.Root)
                && PurityJudge.JudgeChain(constructor, scope, inference.Of) is { } chain)
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
        }

        private void JudgeDefaults(ConcreteType type)
        {
            foreach(var field in Defaultability.InstanceFields(type))
            {
                if(field.Initializer is { } initializer)
                {
                    JudgeDefault(initializer, field, ScopeDescription.FieldInitializer(field.Name, type.Name), type);
                }
            }

            foreach(var property in type.PropertiesList)
            {
                if(property is { Initializer: { } initializer, HasBackingStorage: true, Modifiers.IsStatic: false })
                {
                    JudgeDefault(initializer, property, ScopeDescription.FieldInitializer(property.Name, type.Name), type);
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
                LocalFunctionStatement function => new Origins(function, function.Function, kind, OriginsFor(enclosingOf[function], kind)),
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