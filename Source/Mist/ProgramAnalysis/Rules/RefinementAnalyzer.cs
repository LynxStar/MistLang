using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Discharges a refinement where a value meets one: a constant against the predicate, a refined value
/// against another refinement by implication, and anything else against what <see cref="RefinementFlow"/>
/// has established about the local it is -- a condition assumed on the path, a constant it was given, an
/// increment it took, a parameter's own `where`. What cannot be proven is refused where
/// the value meets the refinement -- a declaration, an assignment, a return, an index into an array
/// whose length is known or that a local or a field holds, an argument at a call, a construction or a
/// constructor chain, and the operand of a cast, which is an argument to its conversion function. A
/// parameter's `where` IS the parameter's type, an anonymous refinement of the declared one, so the
/// model has one way to say a value is narrowed and this rule one way to read it, and nothing is left
/// to a runtime guard at the callee: refinement is type checking, and a call is where the type is
/// met. What the prover cannot read -- a property pattern, a real, an expression that is neither a
/// constant nor a local -- is not refused either.
/// </summary>
internal sealed class RefinementAnalyzer : IProgramAnalyzer
{
    public string Id => "refinement-proving";

    public void Analyze(AnalysisContext context)
    {
        var lifted = new LiftedReturns();

        foreach(var root in context.Bodies)
        {
            new Proving(context, lifted).Judge(FlowGraphBuilder.Build(root.Root), RefinementFlow.Nothing);
        }

        foreach(var list in context.Nodes.OfType<ListPattern>())
        {
            if(list.Reads is { } reads)
            {
                new Proving(context, lifted).NeedElements(list, reads);
            }
        }

        foreach(var closing in context.Model.ConstructorConstraintClosures.Closings)
        {
            if(closing.Parameter.IsDependent && context.IsInSource(closing.Node))
            {
                JudgeClosing(context, closing.Parameter, closing.Argument, closing.Node);
            }
        }

        foreach(var concrete in context.Types.OfType<ConcreteType>())
        {
            JudgeImplementations(context, concrete);

            foreach(var constructor in concrete.ConstructorsList)
            {
                if(constructor is { ChainedConstructor: { } chained, DeclarationNode: { } declared } && context.IsInSource(declared))
                {
                    new Proving(context, lifted).NeedArguments(
                        RefinementFlow.Nothing, constructor.ChainArgumentsList, constructor.ChainParameterTypes, chained.ParametersList);
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

    private sealed class Proving(AnalysisContext context, LiftedReturns lifted)
    {
        public void Judge(FlowGraph graph, ImmutableDictionary<LocalVariable, Knowledge> entry)
        {
            var solution = FlowSolution<ImmutableDictionary<LocalVariable, Knowledge>>.Solve(graph, new RefinementFlow(), entry);
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
                    Need(state, assignment.Value, assignment.Target.Type, assignment.Value, onConstraint: RefinementFindings.Display(assignment.Target));

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

                case { Kind: FlowStepKind.Evaluate, Node: CastExpression cast } when CastAnalyzer.Converts(context, cast):
                    Need(state, cast.Operand, cast.ParameterType ?? cast.TargetType, cast);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: LambdaExpression or AsyncBlockExpression or LocalFunctionStatement } when graph.Nested.TryGetValue(step.Node, out var nested):
                    new Proving(context, lifted).Judge(nested, RefinementFlow.Nothing);
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

                Need(state, arguments[i], parameterTypes[i], arguments[i], onConstraint: parameter?.Name ?? RefinementFindings.Display(arguments[i]));
            }
        }

        private void NeedIndex(ImmutableDictionary<LocalVariable, Knowledge> state, IndexExpression index)
        {
            var receiverType = index.Receiver.Type is TypeAlias { AliasedType: { } aliased } ? aliased : index.Receiver.Type;

            if(receiverType is ConstructedType { Definition: var definition, TypeArguments.Count: 2 } && Lengths.IsIndexable(definition))
            {
                var length = Lengths.Typed(receiverType) ?? RefinementFlow.Held(state, index.Receiver);
                var known = Reaching(state, index, length);

                if(known is null)
                {
                    return;
                }

                var symbolic = new Requirement(Admitted.AtLeast(0), [new SymbolicBound("Length", ComparisonKind.LessThan)]);
                var required = Lengths.Shortest(length) is { } shortest ? new Requirement(Admitted.Between(0, shortest - 1), []) : symbolic;

                if(!Satisfied(known, symbolic, index.Receiver))
                {
                    Need(known, index.Index, Lengths.Exact(length) is null ? symbolic : required, onIndex: index.Receiver, alsoSatisfies: required);
                }

                return;
            }

            if(index.FromEnd is { } counted)
            {
                var written = index.Index is ImplicitConversionExpression conversion ? conversion.Operand : index.Index;
                var position = written is UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd, Operand: var offset }
                    ? Counted(counted).Minus(Known(state, offset).Values)
                    : Admitted.Everything;

                NeedPosition(position, counted, written.Node, RefinementFindings.Display(written), [written]);
                return;
            }

            if(!Requirement.IsIntegral(Requirement.Unrefined(index.Index.Type)))
            {
                return;
            }

            if(index is { ParameterType: { } slot, Indexer.ParametersList: [var parameter] })
            {
                Need(state, index.Index, slot, index.Index, onConstraint: parameter.Name);
            }
        }

        // What is known of the value that meets an array's or a span's slot. `^k` names the position
        // `Length - k`, so its knowledge is the length shifted by k, and below Length by construction
        // whenever k is at least one -- the prover then asks of it what it asks of any other index, and
        // never learns what `^` means. Null for an index no integral value reaches.
        private Knowledge? Reaching(ImmutableDictionary<LocalVariable, Knowledge> state, IndexExpression index, Admitted? length)
        {
            if(index.Index is not UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd, Operand: var offset })
            {
                return Requirement.IsIntegral(Requirement.Unrefined(index.Index.Type)) ? Known(state, index.Index) : null;
            }

            if(Lengths.Exact(Known(state, offset).Values) is not { } back || length is null)
            {
                return Knowledge.Unknown;
            }

            var below = back >= 1 && RefinementFlow.Holder(index.Receiver) is { } holder
                ? ImmutableHashSet.Create(new Bound(holder, ComparisonKind.LessThan))
                : ImmutableHashSet<Bound>.Empty;

            return new Knowledge(length.Shifted(-back), below);
        }

        private void Need(
            ImmutableDictionary<LocalVariable, Knowledge> state, Expression value, IMistType target, Expression at, string? onConstraint = null)
        {
            if(value.Type is ErrorType)
            {
                return;
            }

            var considered = Considered(target, value.Type, value);

            if(considered.Count == 0)
            {
                return;
            }

            if(onConstraint is not null && Requirement.IsAnonymous(target) && Requirement.OfType(target) is { } constrained)
            {
                Need(state, value, constrained, at, onConstraint);
                return;
            }

            var known = Known(state, value);

            if(!AnySatisfied(known, considered))
            {
                RefinementFindings.Report(context, known.Values, value, Requirement.OfType(considered[0])!, TypeDisplay.Mist(target), at, onConstraint: null);
            }
        }

        public void NeedElements(ListPattern list, CountedIndexer reads)
        {
            var elements = list.Elements;
            var slice = -1;

            for(var i = 0; i < elements.Count && slice < 0; i++)
            {
                slice = elements[i].IsSlice ? i : -1;
            }

            var fixedCount = elements.Count - (slice < 0 ? 0 : 1);
            var count = Counted(reads).And(slice < 0 ? Admitted.Exactly(fixedCount) : Admitted.AtLeast(fixedCount));

            if(count.Intervals.Length == 0)
            {
                return;
            }

            for(var i = 0; i < elements.Count; i++)
            {
                if(elements[i] is { IsSlice: false, Subpattern: { } element } && element is not CatchAllPattern { Binding: null })
                {
                    var back = elements.Count - i;
                    var (position, shown) = slice >= 0 && i > slice ? (count.Shifted(-back), $"^{back}") : (Admitted.Exactly(i), $"{i}");

                    NeedPosition(position, reads, element.Node, shown, []);
                }
            }
        }

        private void NeedPosition(Admitted position, CountedIndexer counted, Storm.Runtime.SyntaxNode at, string shown, IReadOnlyList<Expression> about)
        {
            var considered = Considered(counted.ParameterType, counted.Count.Type, value: null);

            if(considered.Count > 0 && !AnySatisfied(new Knowledge(position, ImmutableHashSet<Bound>.Empty), considered))
            {
                RefinementFindings.ReportPosition(
                    context, position, Requirement.OfType(considered[0])!, at, shown, counted.Indexer.ParametersList[0].Name, about);
            }
        }

        private Admitted Counted(CountedIndexer counted)
        {
            return (Requirement.ValuesOf(counted.Count.Type) ?? Admitted.Everything).And(lifted.Of(counted.Count) ?? Admitted.Everything);
        }

        private static List<IMistType> Considered(IMistType target, IMistType valueType, Expression? value)
        {
            var constituents = target is UnionType union ? union.Constituents : [target];

            return constituents
                .Where(constituent => Requirement.Refined(constituent) is not null
                    && (Accepts(constituent, valueType) || (value is not null && ConstantConversion.Fits(value, constituent))))
                .ToList();
        }

        private static bool AnySatisfied(Knowledge known, List<IMistType> considered)
        {
            return considered.Any(constituent => Requirement.OfType(constituent) is not { } required || Satisfied(known, required, onIndex: null));
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

            if(!Satisfied(known, required, onIndex))
            {
                RefinementFindings.Report(context, known.Values, value, required, onIndex is { } receiver ? RefinementFindings.Display(receiver) : "", at, onConstraint, onIndex);
            }
        }

        private void Need(Knowledge known, Expression value, Requirement required, Expression onIndex, Requirement alsoSatisfies)
        {
            if(value.Type is ErrorType || Satisfied(known, required, onIndex) || Satisfied(known, alsoSatisfies, onIndex))
            {
                return;
            }

            RefinementFindings.Report(context, known.Values, value, required, RefinementFindings.Display(onIndex), value, onConstraint: null, onIndex);
        }

        private static bool Satisfied(Knowledge known, Requirement required, Expression? onIndex)
        {
            if(!known.Values.IsSubsetOf(required.Values))
            {
                return false;
            }

            foreach(var bound in required.Bounds)
            {
                if(RefinementFlow.Holder(onIndex) is not { } holder || !known.Bounds.Contains(new Bound(holder, bound.Kind)))
                {
                    return false;
                }
            }

            return true;
        }

        private Knowledge Known(ImmutableDictionary<LocalVariable, Knowledge> state, Expression value)
        {
            if(Requirement.Constant(value) is { } constant)
            {
                return new Knowledge(Admitted.Exactly(constant), ImmutableHashSet<Bound>.Empty);
            }

            var typed = (Requirement.ValuesOf(value.Type) ?? Admitted.Everything).And(lifted.Of(value) ?? Admitted.Everything);

            if(value is not LocalReferenceExpression local)
            {
                return new Knowledge(typed, ImmutableHashSet<Bound>.Empty);
            }

            var flow = state.TryGetValue(local.Local, out var known) ? known : Knowledge.Unknown;

            return new Knowledge(flow.Values.And(typed), flow.Bounds);
        }

        private static bool Accepts(IMistType constituent, IMistType valueType)
        {
            return ConversionClassifier.Classify(Requirement.Unrefined(valueType), Requirement.Unrefined(constituent)) is ConversionKind.Identity or ConversionKind.Implicit;
        }
    }
}