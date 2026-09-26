using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
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
/// met. Exact literal facts also decide supported real, string and regex predicates. A predicate with
/// neither an interval nor a sound literal decision is met only by a value already of that refinement,
/// so anything else must pass an `is` first; one refused at its declaration owes nothing more.
/// </summary>
internal sealed class RefinementAnalyzer : IProgramAnalyzer
{
    public string Id => "refinement-proving";

    public void Analyze(AnalysisContext context)
    {
        var lifted = new LiftedReturns();

        foreach(var root in context.Bodies)
        {
            new Proving(context, lifted, UnseenWrites.Of(root.Root)).Judge(FlowGraphBuilder.Build(root.Root), RefinementFlow.Nothing);
        }

        foreach(var list in context.Nodes.OfType<ListPattern>())
        {
            if(list.Reads is { } reads)
            {
                new Proving(context, lifted, UnseenWrites.None).NeedElements(list, reads);
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
            RefinementContracts.Judge(context, concrete, lifted);

            foreach(var constructor in concrete.ConstructorsList)
            {
                if(constructor is { ChainedConstructor: { } chained, DeclarationNode: { } declared } && context.IsInSource(declared))
                {
                    new Proving(context, lifted, UnseenWrites.None).NeedArguments(
                        RefinementFlow.Nothing, constructor.ChainArgumentsList, constructor.ChainParameterTypes, chained.ParametersList);
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

    private sealed class Proving(AnalysisContext context, LiftedReturns lifted, IReadOnlySet<object> unseenWrites)
    {
        public void Judge(FlowGraph graph, ImmutableDictionary<object, Knowledge> entry)
        {
            var solution = FlowSolution<ImmutableDictionary<object, Knowledge>>.Solve(graph, new RefinementFlow(unseenWrites, lifted), entry);
            solution.Replay((state, step) => Judge(graph, state, step));
        }

        private void Judge(FlowGraph graph, ImmutableDictionary<object, Knowledge> state, FlowStep step)
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
                    NeedArguments(state, call.Arguments, call.ParameterTypes, call.Method?.ParametersList, (call.Receiver as MemberAccessExpression)?.Receiver);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: NewExpression construction }:
                    NeedArguments(state, construction.Arguments, construction.ParameterTypes, construction.Constructor?.ParametersList);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: CastExpression cast } when CastAnalyzer.Converts(context, cast):
                    Need(state, cast.Operand, cast.ParameterType ?? cast.TargetType, cast);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: LambdaExpression or AsyncBlockExpression or LocalFunctionStatement } when graph.Nested.TryGetValue(step.Node, out var nested):
                    new Proving(context, lifted, unseenWrites).Judge(nested, RefinementFlow.Nothing);
                    break;
            }
        }

        public void NeedArguments(
            ImmutableDictionary<object, Knowledge> state, IReadOnlyList<Expression> arguments,
            IReadOnlyList<IMistType>? parameterTypes, IReadOnlyList<Parameter>? parameters, Expression? receiver = null)
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

                Need(state, arguments[i], parameterTypes[i], arguments[i], onConstraint: parameter?.Name ?? RefinementFindings.Display(arguments[i]), measured: Measured(receiver));
            }
        }

        private void NeedIndex(ImmutableDictionary<object, Knowledge> state, IndexExpression index)
        {
            if(RefinementFlow.Refused(state, index.Receiver, lifted))
            {
                return;
            }

            var receiverType = index.Receiver.Type is TypeAlias { AliasedType: { } aliased } ? aliased : index.Receiver.Type;

            if((receiverType is ConstructedType { Definition: var definition, TypeArguments.Count: 2 } && Lengths.IsIndexable(definition))
                || (receiverType.FullyQualifiedName == "Standard.String" && RangeBound.IsIndex(index.Index.Type)))
            {
                var length = RefinementFlow.Length(state, index.Receiver, lifted);

                if(Lengths.IsRangeSlice(index))
                {
                    NeedRange(state, index, length);
                    return;
                }

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

            if(Lengths.IsRangeSlice(index))
            {
                NeedRange(state, index, RefinementFlow.Length(state, index.Receiver, lifted));
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

            if(index is { ParameterType: { } slot, Indexer.ParametersList: [var parameter] })
            {
                Need(state, index.Index, slot, index.Index, onConstraint: parameter.Name);
            }
        }

        private void NeedRange(ImmutableDictionary<object, Knowledge> state, IndexExpression index, Admitted? length)
        {
            if(RefinementFlow.Refused(state, index.Receiver, lifted) || RefinementFlow.Range(state, index.Index, lifted) is not { } range || Lengths.SafetyOf(range, length) != RangeSafety.Violated)
            {
                return;
            }

            context.Report(
                ProgramAnalysisDiagnostics.IndexOutOfBounds, index.Index.Node, [index.Index, index.Receiver],
                RefinementFindings.Display(index.Index), RefinementFindings.Display(index.Receiver), "0 <= start <= end <= Length");
        }

        // What is known of the value that meets an array's or a span's slot. `^k` names the position
        // `Length - k`, so its knowledge is the length less every k it may be, and below Length by
        // construction whenever every k is at least one -- the prover then asks of it what it asks of
        // any other index, and never learns what `^` means. Null for an index the slot's type refuses.
        private Knowledge? Reaching(ImmutableDictionary<object, Knowledge> state, IndexExpression index, Admitted? length)
        {
            if(index.Index is not UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd } && !RangeBound.IsIndex(index.Index.Type))
            {
                return EntersBounds(index) ? Known(state, index.Index) : null;
            }

            var captured = RangeKnowledge.Index(state, index.Index, lifted);

            if(captured is null)
            {
                return RangeBound.IsIndex(index.Index.Type) ? Knowledge.Unknown : EntersBounds(index) ? Known(state, index.Index) : null;
            }

            if(!captured.FromEnd)
            {
                return new Knowledge(captured.Values, ImmutableHashSet<Bound>.Empty);
            }

            var back = captured.Values;

            if(length is null || back.Low is not { } nearest)
            {
                return Knowledge.Unknown;
            }

            var below = nearest >= 1 && RefinementFlow.Holder(index.Receiver) is { } holder
                ? ImmutableHashSet.Create(new Bound(holder, ComparisonKind.LessThan))
                : ImmutableHashSet<Bound>.Empty;

            return new Knowledge(Lengths.Exact(back) is { } exact ? length.Shifted(-exact) : length.Minus(back), below);
        }

        private static bool EntersBounds(IndexExpression index)
        {
            return index.ParameterType is { } bounds
                ? Accepts(bounds, index.Index.Type) || ConstantConversion.Fits(index.Index, bounds)
                : Requirement.IsIntegral(Requirement.Unrefined(index.Index.Type));
        }

        private void Need(
            ImmutableDictionary<object, Knowledge> state, Expression value, IMistType target, Expression at, string? onConstraint = null,
            Expression? measured = null)
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

            if(onConstraint is not null && Requirement.IsAnonymous(target) && !LiteralPredicates.Applies(target) && Requirement.OfType(target) is { } constrained)
            {
                Need(state, value, constrained, at, onConstraint, measured: measured);
                return;
            }

            var known = Known(state, value);

            var literal = LiteralValue.Of(value, held => state.TryGetValue(held, out var exact) ? exact.Literal : null);

            if(AnySatisfied(known, considered, value.Type, measured, Length(state, measured), literal, value))
            {
                return;
            }

            if(LiteralPredicates.Applies(considered[0]))
            {
                RefinementFindings.ReportLiteral(
                    context, value, considered[0], TypeDisplay.Mist(target), at,
                    Requirement.IsAnonymous(target) ? onConstraint : null, literal);
            }
            else if(Requirement.OfType(considered[0]) is { } required)
            {
                RefinementFindings.Report(
                    context, known.Values, value, Stated(Read(required, proving: false), Length(state, measured)), TypeDisplay.Mist(target), at, onConstraint: null);
            }
            else
            {
                RefinementFindings.ReportUnreadable(
                    context, value, considered[0], TypeDisplay.Mist(target), at, Requirement.IsAnonymous(target) ? onConstraint : null);
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

            if(considered.Count == 0 || AnySatisfied(new Knowledge(position, ImmutableHashSet<Bound>.Empty), considered, counted.Count.Type))
            {
                return;
            }

            var parameter = counted.Indexer.ParametersList[0].Name;

            if(Requirement.OfType(considered[0]) is { } required)
            {
                RefinementFindings.ReportPosition(context, position, Read(required, proving: false), at, shown, parameter, about);
            }
            else
            {
                RefinementFindings.ReportPositionUnreadable(context, considered[0], at, shown, parameter, about);
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

        private bool AnySatisfied(
            Knowledge known, List<IMistType> considered, IMistType valueType, Expression? measured = null, Admitted? length = null, LiteralValue? literal = null, Expression? expression = null)
        {
            return considered.Any(constituent => LiteralPredicates.Applies(constituent)
                ? LiteralPredicates.Satisfied(constituent, valueType, literal, expression)
                    || (LiteralPredicates.Decide(constituent, literal) is null && RealKnowledge.Satisfied(known.Real, constituent))
                : Requirement.OfType(constituent) is { } required
                ? Satisfied(known, Read(required, proving: true), measured, length)
                : !Requirement.IsUnreadable(constituent) || ReferenceEquals(Requirement.Refined(valueType), Requirement.Refined(constituent)));
        }

        // A bound naming a member is read through that member's type or lift, which every instance shares:
        // to prove, against the end of the member's interval every value it may take clears; to report,
        // only once the member is one value, so a value it merely might exceed stays unproven.
        private Requirement Read(Requirement required, bool proving)
        {
            var values = required.Values;
            var bounds = ImmutableArray.CreateBuilder<SymbolicBound>();

            foreach(var bound in required.Bounds)
            {
                if(bound.Member is { } member && Limit(bound.Kind, Signature(member), proving) is { } limit)
                {
                    values = values.And(limit);
                }
                else
                {
                    bounds.Add(bound);
                }
            }

            return new Requirement(values, bounds.ToImmutable());
        }

        private static Admitted? Limit(ComparisonKind kind, Admitted member, bool proving)
        {
            if(!proving && Lengths.Exact(member) is null)
            {
                return null;
            }

            return kind switch
            {
                ComparisonKind.LessThan when member.Low is { } low && low > long.MinValue => Admitted.Below(low),
                ComparisonKind.LessThanOrEqual when member.Low is { } low && low > long.MinValue => Admitted.AtMost(low),
                ComparisonKind.GreaterThan when member.High is { } high && high < long.MaxValue => Admitted.Above(high),
                ComparisonKind.GreaterThanOrEqual when member.High is { } high && high < long.MaxValue => Admitted.AtLeast(high),
                _ => null,
            };
        }

        private Admitted Signature(object member)
        {
            return member switch
            {
                Property property => (Requirement.ValuesOf(property.Type) ?? Admitted.Everything).And(lifted.Of(property) ?? Admitted.Everything),
                Field field => Requirement.ValuesOf(field.Type) ?? Admitted.Everything,
                _ => Admitted.Everything,
            };
        }

        private void Need(
            ImmutableDictionary<object, Knowledge> state, Expression value, Requirement required, Expression at,
            string? onConstraint = null, Expression? onIndex = null, Expression? measured = null)
        {
            if(value.Type is ErrorType)
            {
                return;
            }

            var known = Known(state, value);

            if(!Satisfied(known, Read(required, proving: true), onIndex ?? measured, Length(state, measured)))
            {
                RefinementFindings.Report(
                    context, known.Values, value, Stated(Read(required, proving: false), Length(state, measured)),
                    onIndex is { } receiver ? RefinementFindings.Display(receiver) : "", at, onConstraint, onIndex);
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

        private static Expression? Measured(Expression? receiver)
        {
            var receiverType = receiver?.Type is TypeAlias { AliasedType: { } aliased } ? aliased : receiver?.Type;

            return receiverType is ConstructedType { Definition: var definition, TypeArguments.Count: 2 } && Lengths.IsIndexable(definition)
                ? receiver
                : null
                ;
        }

        private Admitted? Length(ImmutableDictionary<object, Knowledge> state, Expression? measured)
        {
            return RefinementFlow.Length(state, measured, lifted);
        }

        private static Requirement Stated(Requirement required, Admitted? length)
        {
            return Lengths.Exact(length) is { } exact && required.Bounds.Any(bound => bound is { Name: "Length", Kind: ComparisonKind.LessThan, Member: null })
                ? new Requirement(
                    required.Values.And(Admitted.Below(exact)), [.. required.Bounds.Where(bound => bound is not { Name: "Length", Kind: ComparisonKind.LessThan, Member: null })])
                : required
                ;
        }

        private static bool Satisfied(Knowledge known, Requirement required, Expression? onIndex, Admitted? length = null)
        {
            if(!known.Values.IsSubsetOf(required.Values))
            {
                return false;
            }

            foreach(var bound in required.Bounds)
            {
                if(bound is { Name: "Length", Kind: ComparisonKind.LessThan, Member: null } && Lengths.Shortest(length) is { } shortest
                    && known.Values.IsSubsetOf(Admitted.Below(shortest)))
                {
                    continue;
                }

                if(RefinementFlow.Holder(onIndex) is not { } holder || !known.Bounds.Contains(new Bound(holder, bound.Kind)))
                {
                    return false;
                }
            }

            return true;
        }

        private Knowledge Known(ImmutableDictionary<object, Knowledge> state, Expression value)
        {
            if(Requirement.Constant(value) is { } constant)
            {
                return new Knowledge(Admitted.Exactly(constant), ImmutableHashSet<Bound>.Empty);
            }

            var typed = (Requirement.ValuesOf(value.Type) ?? Admitted.Everything).And(lifted.Of(value) ?? Admitted.Everything);

            if(value is not LocalReferenceExpression local)
            {
                return new Knowledge(typed, ImmutableHashSet<Bound>.Empty) { Real = RealKnowledge.Of(state, value) };
            }

            var flow = state.TryGetValue(local.Local, out var known) ? known : Knowledge.Unknown;

            return new Knowledge(flow.Values.And(typed), flow.Bounds) { Real = RealKnowledge.Of(state, value) };
        }

        private static bool Accepts(IMistType constituent, IMistType valueType)
        {
            return ConversionClassifier.Classify(Requirement.Unrefined(valueType), Requirement.Unrefined(constituent)) is ConversionKind.Identity or ConversionKind.Implicit;
        }
    }
}
