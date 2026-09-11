using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Discharges a refinement where a value meets one: a constant against the predicate, a refined value
/// against another refinement by implication, and anything else against what the flow has established
/// about the local it is -- a condition assumed on the path, a constant it was given, an increment it
/// took, a parameter's own `where`. The knowledge is an interval set per local on the flow framework,
/// widened to infinity the third time a join moves it, so a counting loop settles while an inner loop
/// revisited by an outer one keeps the bound the outer condition gave, and a symbolic upper bound
/// `i &lt; a.Length` remembered against the array itself, which is what indexing an array of unknown
/// length needs. The array may be a local or a field of this instance; the bound is forgotten when
/// that holder is reassigned and, for a field that is not readonly, at any call, object creation or
/// await, since code the flow cannot see may reassign it then. What cannot be proven is refused where
/// the value meets the refinement -- a declaration, an assignment, a return, an index into an array
/// whose length is known or that a local or a field holds, and an argument at a call, a construction
/// or a constructor chain. A parameter's `where` IS the parameter's type, an anonymous refinement of
/// the declared one, so the model has one way to say a value is narrowed and this rule one way to
/// read it, and nothing is left to a runtime guard at the callee: refinement is type checking, and a
/// call is where the type is met. What the prover cannot read -- a property pattern, a real, an
/// expression that is neither a constant nor a local -- is not refused either.
/// </summary>
internal sealed class RefinementAnalyzer : IProgramAnalyzer
{
    public string Id => "refinement-proving";

    public void Analyze(AnalysisContext context)
    {
        foreach(var root in SourceBodies.Of(context.Model))
        {
            new Proving(context).Judge(FlowGraphBuilder.Build(root.Root), Proving.Nothing);
        }

        foreach(var closing in context.Model.ConstructorConstraintClosures.Closings)
        {
            if(closing.Parameter.IsDependent && context.IsInSource(closing.Node))
            {
                JudgeClosing(context, closing.Parameter, closing.Argument, closing.Node);
            }
        }

        foreach(var concrete in SourceTypes.Of(context.Model).OfType<ConcreteType>())
        {
            JudgeImplementations(context, concrete);

            foreach(var constructor in concrete.ConstructorsList)
            {
                if(constructor is { ChainedConstructor: { } chained, DeclarationNode: { } declared } && context.IsInSource(declared))
                {
                    new Proving(context).NeedArguments(
                        Proving.Nothing, constructor.ChainArgumentsList, constructor.ChainParameterTypes, chained.ParametersList);
                }
            }
        }
    }

    private static void JudgeImplementations(AnalysisContext context, ConcreteType concrete)
    {
        var fillers = ConformanceAnalyzer.Fillers(concrete).ToList();

        foreach(var (abstractType, substitution) in ConformanceAnalyzer.Reached(concrete.BaseAbstractTypes, context.Model.TypeInterner))
        {
            foreach(var slot in abstractType.MethodsList)
            {
                var filler = fillers.FirstOrDefault(candidate => candidate.Method is { } method
                    && method.Name == slot.Name && SlotSignatures.Answers(slot, method, substitution, context.Model.TypeInterner))?.Method;

                if(filler is null || filler.DeclaringType != concrete || filler.DeclarationNode is null)
                {
                    continue;
                }

                for(var i = 0; i < slot.ParametersList.Count && i < filler.ParametersList.Count; i++)
                {
                    if(Requirement.OfType(filler.ParametersList[i].Type) is not { } demanded)
                    {
                        continue;
                    }

                    var promised = Requirement.OfType(slot.ParametersList[i].Type) ?? Requirement.Anything;

                    if(!promised.Values.IsSubsetOf(demanded.Values))
                    {
                        context.Report(
                            ProgramAnalysisDiagnostics.ConstraintStrengthened, filler.DeclarationNode, [],
                            filler.Name, filler.ParametersList[i].Name, promised.Values, demanded.Values);
                    }
                }
            }
        }
    }

    private static void JudgeClosing(AnalysisContext context, TypeParameter parameter, IMistType argument, Storm.Runtime.SyntaxNode at)
    {
        if(Demanded(parameter) is not { } required || required.Values.IsEverything)
        {
            return;
        }

        switch(argument)
        {
            case LiftedType { Value: long value } when !required.Values.Contains(value):
                context.Report(ProgramAnalysisDiagnostics.DependentArgumentViolated, at, [], value, required.Values, parameter.Name);
                break;

            case TypeParameter forwarded when !(Demanded(forwarded)?.Values ?? Admitted.Everything).IsSubsetOf(required.Values):
                context.Report(
                    ProgramAnalysisDiagnostics.DependentForwardUnproven, at, [],
                    forwarded.Name, parameter.Name, Demanded(forwarded) is { } own ? $"'{own.Values}'" : "an unconstrained parameter", required.Values);
                break;
        }
    }

    private static Requirement? Demanded(TypeParameter parameter)
    {
        var declared = Requirement.Of(parameter.Refinement);
        var domain = parameter.Domain is { } type ? Requirement.OfType(type) : null;

        return (declared, domain) switch
        {
            (null, null) => null,
            ({ } only, null) => only,
            (null, { } alone) => alone,
            ({ } both, { } and) => both.And(and),
        };
    }

    private sealed record Bound(object Holder, ComparisonKind Kind);

    private sealed record Knowledge(Admitted Values, ImmutableHashSet<Bound> Bounds, int Changes = 0)
    {
        public static readonly Knowledge Unknown = new(Admitted.Everything, ImmutableHashSet<Bound>.Empty);
    }

    private sealed class Proving(AnalysisContext context) : IFlowLattice<ImmutableDictionary<LocalVariable, Knowledge>>
    {
        public static readonly ImmutableDictionary<LocalVariable, Knowledge> Nothing =
            ImmutableDictionary.Create<LocalVariable, Knowledge>(ReferenceEqualityComparer.Instance);

        public ImmutableDictionary<LocalVariable, Knowledge> Join(
            ImmutableDictionary<LocalVariable, Knowledge> left, ImmutableDictionary<LocalVariable, Knowledge> right)
        {
            var joined = Nothing;

            foreach(var (local, mine) in left)
            {
                if(!right.TryGetValue(local, out var theirs))
                {
                    continue;
                }

                var values = mine.Values.Or(theirs.Values);
                var changes = mine.Changes;

                if(!values.Equals(mine.Values))
                {
                    changes++;
                    values = changes > 2 ? values.WidenedAgainst(mine.Values) : values;
                }

                joined = joined.Add(local, new Knowledge(values, mine.Bounds.Intersect(theirs.Bounds), changes));
            }

            return joined;
        }

        public bool Same(ImmutableDictionary<LocalVariable, Knowledge> left, ImmutableDictionary<LocalVariable, Knowledge> right)
        {
            return left.Count == right.Count
                && left.All(entry => right.TryGetValue(entry.Key, out var other)
                    && entry.Value.Values.Equals(other.Values) && entry.Value.Bounds.SetEquals(other.Bounds));
        }

        public ImmutableDictionary<LocalVariable, Knowledge> Transfer(ImmutableDictionary<LocalVariable, Knowledge> state, FlowStep step)
        {
            switch(step)
            {
                case { Kind: FlowStepKind.Assume, Node: Expression condition }:
                    return Learn(state, condition, step.Holds);

                case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration }:
                    return Given(state, declaration.Local, declaration.Initializer);

                case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false, Target: LocalReferenceExpression target } assignment }:
                    state = Forget(state, holder => ReferenceEquals(holder, target.Local));

                    if(assignment.Value is BinaryExpression { Left: LocalReferenceExpression { Local: var same }, OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Subtract } step2
                        && ReferenceEquals(same, target.Local) && Requirement.Constant(step2.Right) is { } by && state.TryGetValue(target.Local, out var before))
                    {
                        return state.SetItem(target.Local, new Knowledge(before.Values.Shifted(step2.OperatorKind == BinaryOperatorKind.Add ? by : -by), ImmutableHashSet<Bound>.Empty));
                    }

                    return Given(state, target.Local, assignment.Value);

                case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { Target: var target } } when Assigned(target) is { } field:
                    return Forget(state, holder => ReferenceEquals(holder, field));

                case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression or NewExpression or AwaitExpression }:
                    return Forget(state, holder => holder is Field { Modifiers.IsReadonly: false });

                default:
                    return state;
            }
        }

        public void Judge(FlowGraph graph, ImmutableDictionary<LocalVariable, Knowledge> entry)
        {
            var solution = FlowSolution<ImmutableDictionary<LocalVariable, Knowledge>>.Solve(graph, this, entry);
            solution.Replay((state, step) => Judge(graph, state, step));
        }

        private void Judge(FlowGraph graph, ImmutableDictionary<LocalVariable, Knowledge> state, FlowStep step)
        {
            switch(step)
            {
                case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: { } initializer } declaration }:
                    Need(state, initializer, declaration.Local.Type, initializer);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false } assignment }:
                    if(Requirement.IsAnonymous(assignment.Target.Type) && Requirement.OfType(assignment.Target.Type) is { } constrained)
                    {
                        Need(state, assignment.Value, constrained, assignment.Value, onConstraint: Display(assignment.Target));
                    }
                    else
                    {
                        Need(state, assignment.Value, assignment.Target.Type, assignment.Value);
                    }

                    if(assignment.Target is IndexExpression written)
                    {
                        NeedIndex(state, written);
                    }

                    break;

                case { Kind: FlowStepKind.Evaluate, Node: ReturnStatement { Value: { } value, TargetType: { } target } }:
                    Need(state, value, target, value);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: IndexExpression index }:
                    NeedIndex(state, index);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression call }:
                    NeedArguments(state, call.Arguments, call.ParameterTypes, call.Method?.ParametersList);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: NewExpression construction }:
                    NeedArguments(state, construction.Arguments, construction.ParameterTypes, construction.Constructor?.ParametersList);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: LambdaExpression or AsyncBlockExpression or LocalFunctionStatement } when graph.Nested.TryGetValue(step.Node, out var nested):
                    new Proving(context).Judge(nested, Nothing);
                    break;
            }
        }

        public void NeedArguments(
            ImmutableDictionary<LocalVariable, Knowledge> state, IReadOnlyList<Expression> arguments,
            IReadOnlyList<IMistType>? parameterTypes, IReadOnlyList<Parameter>? parameters)
        {
            if(parameterTypes is null)
            {
                return;
            }

            for(var i = 0; i < arguments.Count && i < parameterTypes.Count; i++)
            {
                var parameter = parameters is not null && i < parameters.Count ? parameters[i] : null;

                if(parameter is { IsFlattened: true })
                {
                    return;
                }

                if(parameter is { Modifier.Direction: ByRefDirection.Out })
                {
                    continue;
                }

                if(Requirement.IsAnonymous(parameterTypes[i]) && Requirement.OfType(parameterTypes[i]) is { } demanded)
                {
                    Need(state, arguments[i], demanded, arguments[i], onConstraint: parameter?.Name ?? Display(arguments[i]));
                }
                else
                {
                    Need(state, arguments[i], parameterTypes[i], arguments[i]);
                }
            }
        }

        private void NeedIndex(ImmutableDictionary<LocalVariable, Knowledge> state, IndexExpression index)
        {
            if(!IsIntegral(Unrefined(index.Index.Type)))
            {
                return;
            }

            var receiverType = index.Receiver.Type is TypeAlias { AliasedType: { } aliased } ? aliased : index.Receiver.Type;

            if(receiverType is ConstructedType { Definition: var definition, TypeArguments.Count: 2 } && IsIndexable(definition))
            {
                if(KnownLength(receiverType) is { } length)
                {
                    Need(state, index.Index, new Requirement(Admitted.Between(0, length - 1), []), index.Index, onIndex: index.Receiver);
                }
                else if(Holder(index.Receiver) is not null)
                {
                    Need(state, index.Index, new Requirement(Admitted.AtLeast(0), [new SymbolicBound("Length", ComparisonKind.LessThan)]), index.Index, onIndex: index.Receiver);
                }

                return;
            }

            var indexer = (receiverType is ConstructedType other ? other.Definition : receiverType) is MemberType member ? member.IndexersList.FirstOrDefault() : null;

            if(indexer is { ParametersList: [var parameter] } && Requirement.OfType(parameter.Type) is { } constraint)
            {
                Need(state, index.Index, constraint, index.Index, onConstraint: parameter.Name);
            }
        }

        private void Need(ImmutableDictionary<LocalVariable, Knowledge> state, Expression value, IMistType target, Expression at)
        {
            if(value.Type is ErrorType)
            {
                return;
            }

            var constituents = target is UnionType union ? union.Constituents : [target];
            var refined = constituents.Where(constituent => Requirement.Refined(constituent) is not null).ToList();

            if(refined.Count == 0)
            {
                return;
            }

            var known = Known(state, value);
            var constant = Requirement.Constant(value) is not null;
            var considered = refined
                .Where(constituent => Accepts(constituent, value.Type) || (constant && IsIntegral(Unrefined(constituent)) && IsIntegral(Unrefined(value.Type))))
                .ToList();

            if(considered.Count == 0)
            {
                return;
            }

            foreach(var constituent in considered)
            {
                if(Requirement.OfType(constituent) is not { } required || Satisfied(known, required, onIndex: null))
                {
                    return;
                }
            }

            Report(known, value, Requirement.OfType(considered[0])!, TypeDisplay.Mist(target), at, onConstraint: null);
        }

        private void Need(
            ImmutableDictionary<LocalVariable, Knowledge> state, Expression value, Requirement required, Expression at,
            string? onConstraint = null, Expression? onIndex = null)
        {
            if(value.Type is ErrorType)
            {
                return;
            }

            var known = Known(state, value);

            if(!Satisfied(known, required, onIndex, constant: Requirement.Constant(value) is not null))
            {

                Report(known, value, required, onIndex is { } receiver ? Display(receiver) : "", at, onConstraint, onIndex);
            }
        }

        private static bool Satisfied(Knowledge known, Requirement required, Expression? onIndex, bool constant = false)
        {
            if(!known.Values.IsSubsetOf(required.Values))
            {
                return false;
            }

            foreach(var bound in required.Bounds)
            {
                if(!constant && (Holder(onIndex) is not { } holder || !known.Bounds.Contains(new Bound(holder, bound.Kind))))
                {
                    return false;
                }
            }

            return true;
        }

        private void Report(Knowledge known, Expression value, Requirement required, string target, Expression at, string? onConstraint, Expression? onIndex = null)
        {
            var predicate = Describe(required);

            if(onIndex is not null)
            {
                if(Requirement.Constant(value) is { } index)
                {
                    context.Report(ProgramAnalysisDiagnostics.IndexOutOfBounds, at.Node, [value, onIndex], index, target, predicate);
                }
                else
                {
                    context.Report(ProgramAnalysisDiagnostics.IndexUnproven, at.Node, [value, onIndex], Display(value), target, predicate);
                }
            }
            else if(onConstraint is not null)
            {
                if(Requirement.Constant(value) is { } constant)
                {
                    context.Report(ProgramAnalysisDiagnostics.ConstraintViolated, at.Node, [value], constant, predicate, onConstraint);
                }
                else
                {
                    context.Report(ProgramAnalysisDiagnostics.ConstraintUnproven, at.Node, [value], Display(value), predicate, onConstraint);
                }
            }
            else if(Requirement.Constant(value) is { } constant)
            {
                context.Report(ProgramAnalysisDiagnostics.RefinementViolated, at.Node, [value], constant, predicate, target);
            }
            else if(Requirement.Refined(value.Type) is not null)
            {
                context.Report(ProgramAnalysisDiagnostics.RefinementNotImplied, at.Node, [value], TypeDisplay.Mist(value.Type), target, known.Values, predicate);
            }
            else
            {
                context.Report(ProgramAnalysisDiagnostics.RefinementUnproven, at.Node, [value], TypeDisplay.Mist(value.Type), target, predicate);
            }
        }

        private static Knowledge Known(ImmutableDictionary<LocalVariable, Knowledge> state, Expression value)
        {
            if(Requirement.Constant(value) is { } constant)
            {
                return new Knowledge(Admitted.Exactly(constant), ImmutableHashSet<Bound>.Empty);
            }

            var typed = Requirement.OfType(value.Type)?.Values ?? Admitted.Everything;

            if(value is not LocalReferenceExpression local)
            {
                return new Knowledge(typed, ImmutableHashSet<Bound>.Empty);
            }

            var flow = state.TryGetValue(local.Local, out var known) ? known : Knowledge.Unknown;

            return new Knowledge(flow.Values.And(typed), flow.Bounds);
        }

        private static ImmutableDictionary<LocalVariable, Knowledge> Learn(
            ImmutableDictionary<LocalVariable, Knowledge> state, Expression condition, bool holds)
        {
            switch(condition)
            {
                case BinaryExpression { Left: LocalReferenceExpression subject, Right: var bound } comparison when Comparison(comparison.OperatorKind) is { } kind:
                    if(Requirement.Constant(bound) is { } constant)
                    {
                        return Narrow(state, subject.Local, holds ? Requirement.Interval(kind, constant) : Requirement.Interval(kind, constant).Not());
                    }

                    var upper = holds ? kind : Flipped(kind);

                    if(bound is MemberAccessExpression { MemberName: "Length", Receiver: var measured }
                        && upper is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual)
                    {
                        if(KnownLength(measured.Type) is { } length)
                        {
                            return Narrow(state, subject.Local, Requirement.Interval(upper, length));
                        }

                        if(Holder(measured) is { } holder)
                        {
                            var known = state.TryGetValue(subject.Local, out var existing) ? existing : Knowledge.Unknown;
                            return state.SetItem(subject.Local, known with { Bounds = known.Bounds.Add(new Bound(holder, upper)) });
                        }
                    }

                    return state;

                case BinaryExpression { Left: var limit, Right: LocalReferenceExpression mirrored } flipped
                    when Comparison(flipped.OperatorKind) is { } mirroredKind && Requirement.Constant(limit) is { } value:
                    return Narrow(state, mirrored.Local, holds ? Requirement.Interval(Mirrored(mirroredKind), value) : Requirement.Interval(Mirrored(mirroredKind), value).Not());

                case IsPatternExpression { Scrutinee: LocalReferenceExpression subject, Pattern: var pattern }
                    when Requirement.Of(pattern) is { Bounds.Length: 0 } matched:
                    return Narrow(state, subject.Local, holds ? matched.Values : matched.Values.Not());

                default:
                    return state;
            }
        }

        private static ImmutableDictionary<LocalVariable, Knowledge> Narrow(
            ImmutableDictionary<LocalVariable, Knowledge> state, LocalVariable local, Admitted values)
        {
            var known = state.TryGetValue(local, out var existing) ? existing : Knowledge.Unknown;
            return state.SetItem(local, known with { Values = known.Values.And(values) });
        }

        private static ImmutableDictionary<LocalVariable, Knowledge> Given(
            ImmutableDictionary<LocalVariable, Knowledge> state, LocalVariable local, Expression? value)
        {
            if(value is null)
            {
                return state.Remove(local);
            }

            if(Requirement.Constant(value) is { } constant)
            {
                return state.SetItem(local, new Knowledge(Admitted.Exactly(constant), ImmutableHashSet<Bound>.Empty));
            }

            if(value is LocalReferenceExpression source && state.TryGetValue(source.Local, out var copied))
            {
                return state.SetItem(local, copied);
            }

            return state.Remove(local);
        }

        private static ImmutableDictionary<LocalVariable, Knowledge> Forget(ImmutableDictionary<LocalVariable, Knowledge> state, Func<object, bool> lost)
        {
            foreach(var (other, known) in state)
            {
                if(known.Bounds.Any(bound => lost(bound.Holder)))
                {
                    state = state.SetItem(other, known with { Bounds = known.Bounds.Where(bound => !lost(bound.Holder)).ToImmutableHashSet() });
                }
            }

            return state;
        }

        private static object? Holder(Expression? array)
        {
            return array switch
            {
                LocalReferenceExpression local => local.Local,
                FieldReferenceExpression field => field.Field,
                MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult { Field: var field } } => field,
                _ => null,
            };
        }

        private static Field? Assigned(Expression target)
        {
            return target switch
            {
                FieldReferenceExpression field => field.Field,
                MemberAccessExpression { Member: FieldResult { Field: var field } } => field,
                _ => null,
            };
        }

        private static bool Accepts(IMistType constituent, IMistType valueType)
        {
            return ConversionClassifier.Classify(Unrefined(valueType), Unrefined(constituent)) is ConversionKind.Identity or ConversionKind.Implicit;
        }

        private static IMistType Unrefined(IMistType type)
        {
            return type switch
            {
                RefinedType refined => Unrefined(refined.BaseType),
                TypeAlias { AliasedType: { } aliased } => Unrefined(aliased),
                _ => type,
            };
        }

        private static long? KnownLength(IMistType type)
        {
            var unaliased = type is TypeAlias { AliasedType: { } aliased } ? aliased : type;

            if(unaliased is not ConstructedType { Definition: var definition, TypeArguments: [_, LiftedType { Value: var length }] } || !IsIndexable(definition))
            {
                return null;
            }

            return length switch
            {
                long known => known,
                int known => known,
                _ => null,
            };
        }

        private static bool IsIntegral(IMistType type)
        {
            return type.FullyQualifiedName is var name
                && (name == WellKnownTypes.Int32.FullyQualifiedName || name == WellKnownTypes.Int64.FullyQualifiedName
                    || name == WellKnownTypes.Int16.FullyQualifiedName || name == WellKnownTypes.UInt32.FullyQualifiedName
                    || name == WellKnownTypes.UInt64.FullyQualifiedName || name == WellKnownTypes.UInt16.FullyQualifiedName
                    || name == WellKnownTypes.Byte.FullyQualifiedName || name == WellKnownTypes.SByte.FullyQualifiedName
                    || name == WellKnownTypes.Char.FullyQualifiedName);
        }

        private static bool IsIndexable(IMistType definition)
        {
            return definition.FullyQualifiedName is "Standard.Array<,>" or "Standard.Span<,>" or "Standard.ReadOnlySpan<,>";
        }

        private static ComparisonKind? Comparison(BinaryOperatorKind kind)
        {
            return kind switch
            {
                BinaryOperatorKind.Equal => ComparisonKind.Equal,
                BinaryOperatorKind.LessThan => ComparisonKind.LessThan,
                BinaryOperatorKind.GreaterThan => ComparisonKind.GreaterThan,
                BinaryOperatorKind.LessThanOrEqual => ComparisonKind.LessThanOrEqual,
                BinaryOperatorKind.GreaterThanOrEqual => ComparisonKind.GreaterThanOrEqual,
                _ => null,
            };
        }

        private static ComparisonKind Mirrored(ComparisonKind kind)
        {
            return kind switch
            {
                ComparisonKind.LessThan => ComparisonKind.GreaterThan,
                ComparisonKind.GreaterThan => ComparisonKind.LessThan,
                ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
                ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
                _ => kind,
            };
        }

        private static ComparisonKind Flipped(ComparisonKind kind)
        {
            return kind switch
            {
                ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
                ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
                ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
                ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThan,
                _ => kind,
            };
        }

        private static string Describe(Requirement required)
        {
            var parts = new List<string>();

            if(!required.Values.IsEverything)
            {
                parts.Add(required.Values.ToString());
            }

            foreach(var bound in required.Bounds)
            {
                parts.Add($"{Symbol(bound.Kind)} {bound.Name}");
            }

            return parts.Count == 0 ? "anything" : string.Join(" and ", parts);
        }

        private static string Symbol(ComparisonKind kind)
        {
            return kind switch
            {
                ComparisonKind.LessThan => "<",
                ComparisonKind.LessThanOrEqual => "<=",
                ComparisonKind.GreaterThan => ">",
                ComparisonKind.GreaterThanOrEqual => ">=",
                _ => "==",
            };
        }

        private static string Display(Expression expression)
        {
            return expression switch
            {
                LocalReferenceExpression local => local.Local.Name,
                FieldReferenceExpression field => field.Field.Name,
                ThisExpression => "this",
                MemberAccessExpression access => $"{Display(access.Receiver)}.{access.MemberName}",
                _ => TypeDisplay.Mist(expression.Type),
            };
        }
    }
}