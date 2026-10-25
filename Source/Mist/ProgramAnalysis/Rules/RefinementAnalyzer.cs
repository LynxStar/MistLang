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
/// constructor chain, an initializer entry -- a member's value, or a collection element as the argument
/// to its selected Add, judged right after it is evaluated -- and the operand of a cast, which is an
/// argument to its conversion function. A
/// parameter's `where` IS the parameter's type, an anonymous refinement of the declared one, so the
/// model has one way to say a value is narrowed and this rule one way to read it, and nothing is left
/// to a runtime guard at the callee: refinement is type checking, and a call is where the type is
/// met. Exact literal facts also decide supported real, string and regex predicates. A predicate with
/// neither an interval nor a sound literal decision is met only by a value already of that refinement,
/// so anything else must pass an `is` first; one refused at its declaration owes nothing more.
/// </summary>
internal sealed class RefinementAnalyzer(TimeSpan matchLimit) : IProgramAnalyzer
{
    public RefinementAnalyzer()
        : this(LiteralPredicates.MatchLimit)
    {
    }

    public string Id => "refinement-proving";

    public void Analyze(AnalysisContext context)
    {
        var lifted = new LiftedReturns();
        var effects = new FieldEffects();

        foreach(var root in context.Bodies)
        {
            new Proving(context, lifted, effects, UnseenWrites.Of(root.Root), matchLimit).Judge(FlowGraphBuilder.Build(root.Root), RefinementFlow.Nothing);
        }

        foreach(var list in context.Nodes.OfType<ListPattern>())
        {
            if(list.Reads is { } reads)
            {
                new Proving(context, lifted, effects, UnseenWrites.None, matchLimit).NeedElements(list, reads);
            }

            if(list.Slice is { Method: { } slice } sliced)
            {
                new Proving(context, lifted, effects, UnseenWrites.None, matchLimit).NeedSliceElements(list, sliced, slice);
            }
        }

        var closings = context.Model.ConstructorConstraintClosures;

        foreach(var closing in closings.Closings.Concat(closings.OpenClosings))
        {
            if(closing.Parameter is { IsDependent: true, IsPack: false } && context.IsInSource(closing.Node))
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
                    new Proving(context, lifted, effects, UnseenWrites.None, matchLimit).NeedArguments(
                        RefinementFlow.Nothing, constructor.ChainArgumentsList, constructor.ChainParameterTypes, chained.ParametersList, callee: chained);
                }
            }
        }
    }

    private static void JudgeClosing(AnalysisContext context, TypeParameter parameter, IMistType argument, Storm.Runtime.SyntaxNode at)
    {
        if(DependentFinding(parameter, argument) is not { } finding)
        {
            return;
        }

        if(finding == ProgramAnalysisDiagnostics.DependentArgumentNotAssignable)
        {
            context.Report(
                finding, at, [],
                argument is LiftedType lifted ? DependentDomains.Spell(lifted.Value) : TypeDisplay.Mist(argument),
                argument is LiftedType value ? DependentDomains.TypeOfValue(value.Value) : WellKnownTypes.Type.Name,
                parameter.Name, TypeDisplay.Mist(parameter.Domain!));
        }
        else if(finding == ProgramAnalysisDiagnostics.DependentForwardNotConvertible && argument is TypeParameter { Domain: { } from } source)
        {
            context.Report(
                finding, at, [], source.Name, TypeDisplay.Mist(from), Owner(source), parameter.Name, TypeDisplay.Mist(parameter.Domain!), Owner(parameter));
        }
        else if(Demanded(parameter) is { } required)
        {
            switch(argument)
            {
                case LiftedType lifted:
                    context.Report(finding, at, [], DependentDomains.Spell(lifted.Value), required.Values, parameter.Name);
                    break;

                case TypeParameter forwarded:
                    context.Report(
                        finding, at, [],
                        forwarded.Name, parameter.Name, Demanded(forwarded) is { } own ? $"'{own.Values}'" : "an unconstrained parameter", required.Values);
                    break;
            }
        }
    }

    public static MistDiagnostic? DependentFinding(TypeParameter parameter, IMistType argument)
    {
        if(DomainFinding(parameter, argument) is { } mismatch)
        {
            return mismatch;
        }

        if(Demanded(parameter) is not { } required || required.Values.IsEverything)
        {
            return null;
        }

        return argument switch
        {
            LiftedType lifted when Known(lifted) is { } values && !values.IsSubsetOf(required.Values)
                => ProgramAnalysisDiagnostics.DependentArgumentViolated,
            TypeParameter forwarded when !(Demanded(forwarded)?.Values ?? Admitted.Everything).IsSubsetOf(required.Values)
                => ProgramAnalysisDiagnostics.DependentForwardUnproven,
            _ => null,
        };
    }

    public static MistDiagnostic? DomainFinding(TypeParameter parameter, IMistType argument)
    {
        if(parameter is not { IsDependent: true, IsPack: false, Domain: { } domain } || DependentDomains.Primitive(domain) is null)
        {
            return null;
        }

        return argument switch
        {
            ErrorType or UnknownType or UnknownValueType => null,
            LiftedType lifted => DependentDomains.Holds(domain, lifted.Value) ? null : ProgramAnalysisDiagnostics.DependentArgumentNotAssignable,
            TypeParameter { IsDependent: true, IsPack: false, Domain: null } => null,
            TypeParameter { IsDependent: true, IsPack: false, Domain: { } from }
                => DependentDomains.Forwards(from, domain) ? null : ProgramAnalysisDiagnostics.DependentForwardNotConvertible,
            _ => ProgramAnalysisDiagnostics.DependentArgumentNotAssignable,
        };
    }

    // Only ulong's upper half is boxed as a ulong, and long.MaxValue is the top every interval saturates at.
    private static Admitted? Known(LiftedType lifted)
    {
        return lifted.Value is ulong ? Admitted.Exactly(long.MaxValue) : Requirement.ValuesOf(lifted);
    }

    private static string Owner(TypeParameter parameter)
    {
        return parameter.DeclaringType is { } owner ? TypeDisplay.Mist(owner) : parameter.Name;
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

    private sealed class Proving(AnalysisContext context, LiftedReturns lifted, FieldEffects effects, IReadOnlySet<object> unseenWrites, TimeSpan matchLimit)
    {
        public void Judge(FlowGraph graph, ImmutableDictionary<object, Knowledge> entry)
        {
            var solution = FlowSolution<ImmutableDictionary<object, Knowledge>>.Solve(graph, new RefinementFlow(unseenWrites, lifted, effects), entry);
            solution.Replay((state, step) => Judge(graph, state, step));
        }

        private void Judge(FlowGraph graph, ImmutableDictionary<object, Knowledge> state, FlowStep step)
        {
            switch(step)
            {
                case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: { } initializer } declaration }:
                    Need(state, initializer, declaration.Local.Type, initializer);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false } assignment }
                    when StorageLocations.NotAssignable(assignment.Target) is null:
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
                    NeedArguments(state, call.Arguments, call.ParameterTypes, call.Parameters, (call.Receiver as MemberAccessExpression)?.Receiver, call.Signature);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: NewExpression construction }:
                    NeedArguments(state, construction.Arguments, construction.ParameterTypes, construction.Constructor?.ParametersList, callee: construction.Constructor);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: ObjectInitializerAssignment { IsTargetFailed: false, Add: { } add } element }:
                    NeedArguments(state, [element.Value], element.AddParameterTypes, add.ParametersList, callee: add);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: ObjectInitializerAssignment { IsTargetFailed: false, TargetType: { } member } entry }:
                    Need(state, entry.Value, member, entry.Value, onConstraint: entry.MemberName);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: CastExpression cast } when CastAnalyzer.Converts(context, cast):
                    Need(state, cast.Operand, cast.ParameterType ?? cast.TargetType, cast);
                    break;

                case { Kind: FlowStepKind.Evaluate, Node: LambdaExpression or AsyncBlockExpression or LocalFunctionStatement } when graph.Nested.TryGetValue(step.Node, out var nested):
                    new Proving(context, lifted, effects, unseenWrites, matchLimit).Judge(nested, RefinementFlow.Nothing);
                    break;
            }
        }

        public void NeedArguments(
            ImmutableDictionary<object, Knowledge> state, IReadOnlyList<Expression> arguments,
            IReadOnlyList<IMistType>? parameterTypes, IReadOnlyList<Parameter>? parameters, Expression? receiver = null, Method? callee = null)
        {
            if(parameterTypes is null)
            {
                return;
            }

            var flattened = parameters is not null && ValuePack.FlattenedSlot(parameters) is not null;
            var reading = Reading.Of(state, arguments, callee?.SourceDeclaration ?? callee, flattened, this);
            var targets = flattened
                ? ArgumentTargets.Of(
                    parameters, parameterTypes, [.. arguments.Select(argument => argument is SpreadExpression ? null : argument.Type)],
                    context.Model.TypeInterner)
                : parameterTypes;

            for(var i = 0; targets is not null && i < arguments.Count && i < targets.Count; i++)
            {
                var slot = flattened ? ValuePack.SlotOf(parameters!, arguments.Count, i) : i;
                var parameter = parameters is not null && slot < parameters.Count ? parameters[slot] : null;

                if(parameter is { Modifier.Direction: ByRefDirection.Out }
                    || targets[i] is not { } target
                    || (flattened && arguments[i] is SpreadExpression))
                {
                    continue;
                }

                Need(
                    state, arguments[i], target, arguments[i], onConstraint: parameter?.Name ?? RefinementFindings.Display(arguments[i]), measured: Measured(receiver),
                    reading: reading);
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

            if(index.Slice is { Method: { } slice } sliced)
            {
                var (start, length) = RefinementFlow.Range(state, index.Index, lifted) is { } range
                    ? Offsets(range, Counted(sliced.Count))
                    : (Admitted.Everything, Admitted.Everything);
                var shown = RefinementFindings.Display(index.Index);

                NeedSliceArguments(slice, sliced.Count, start, length, index.Index.Node, ($"the start of {shown}", $"the length of {shown}"), [index.Index]);
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
            Expression? measured = null, Reading? reading = null)
        {
            reading ??= Reading.None;

            if(value.Type is ErrorType)
            {
                return;
            }

            var considered = Considered(target, value.Type, value);

            if(considered.Count == 0)
            {
                return;
            }

            if(onConstraint is not null && Requirement.IsAnonymous(target) && !LiteralPredicates.Applies(target)
                && Requirement.OfType(target) is { Bounds.Length: > 0 } constrained)
            {
                Need(state, value, constrained, at, onConstraint, measured: measured, reading: reading);
                return;
            }

            var known = Known(state, value);

            var literal = LiteralValue.Of(value, held => state.TryGetValue(held, out var exact) ? exact.Literal : null);

            var decided = considered.All(constituent => Requirement.OfType(constituent) is not { Bounds.Length: > 0 });

            if(decided
                ? RefinementDecision.Judge(value, target, known.Values, literal, known.Real, onConstraint is not null, matchLimit) is null
                : AnySatisfied(known, considered, value.Type, reading, measured, Length(state, measured), literal, value))
            {
                return;
            }

            if(LiteralPredicates.Applies(considered[0]))
            {
                RefinementFindings.ReportLiteral(
                    context, value, considered[0], TypeDisplay.Mist(target), at,
                    Requirement.IsAnonymous(target) ? onConstraint : null, literal, matchLimit);
            }
            else if(Requirement.OfType(considered[0]) is { } required)
            {
                RefinementFindings.Report(
                    context, known.Values, value, Stated(Read(required, proving: false, reading), Length(state, measured)), TypeDisplay.Mist(target), at,
                    onConstraint: Requirement.IsAnonymous(target) ? onConstraint : null);
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

        public void NeedSliceElements(ListPattern list, CountedSlice sliced, Method slice)
        {
            var elements = list.Elements;
            var at = 0;

            while(at < elements.Count && !elements[at].IsSlice)
            {
                at++;
            }

            var fixedCount = elements.Count - 1;
            var count = Counted(sliced.Count).And(Admitted.AtLeast(fixedCount));

            if(at == elements.Count || count.Intervals.Length == 0)
            {
                return;
            }

            var node = elements[at].Subpattern?.Node ?? list.Node;
            NeedSliceArguments(slice, sliced.Count, Admitted.Exactly(at), count.Shifted(-fixedCount), node, ($"{at}", "the length of the slice"), []);
        }

        private void NeedSliceArguments(
            Method slice, Property count, Admitted start, Admitted length, Storm.Runtime.SyntaxNode at, (string Start, string Length) shown, IReadOnlyList<Expression> about)
        {
            if(slice.ParametersList is [var first, var second])
            {
                NeedSlot(start, first.Type, count.Type, first.Name, at, shown.Start, about);
                NeedSlot(length, second.Type, count.Type, second.Name, at, shown.Length, about);
            }
        }

        private static (Admitted Start, Admitted Length) Offsets(StoredRange range, Admitted count)
        {
            var start = range.Start.FromEnd ? count.Minus(range.Start.Values) : range.Start.Values;
            var end = range.End.FromEnd ? count.Minus(range.End.Values) : range.End.Values;

            return (start, end.Minus(start));
        }

        private void NeedPosition(Admitted position, CountedIndexer counted, Storm.Runtime.SyntaxNode at, string shown, IReadOnlyList<Expression> about)
        {
            NeedSlot(position, counted.ParameterType, counted.Count.Type, counted.Indexer.ParametersList[0].Name, at, shown, about);
        }

        private void NeedSlot(Admitted value, IMistType slot, IMistType valueType, string parameter, Storm.Runtime.SyntaxNode at, string shown, IReadOnlyList<Expression> about)
        {
            var considered = Considered(slot, valueType, value: null);

            if(considered.Count == 0 || AnySatisfied(new Knowledge(value, ImmutableHashSet<Bound>.Empty), considered, valueType, Reading.None))
            {
                return;
            }

            if(Requirement.OfType(considered[0]) is { } required)
            {
                RefinementFindings.ReportPosition(context, value, Read(required, proving: false, Reading.None), at, shown, parameter, about);
            }
            else
            {
                RefinementFindings.ReportPositionUnreadable(context, considered[0], at, shown, parameter, about);
            }
        }

        private Admitted Counted(CountedIndexer counted)
        {
            return Counted(counted.Count);
        }

        private Admitted Counted(Property count)
        {
            return (Requirement.ValuesOf(count.Type) ?? Admitted.Everything).And(lifted.Of(count) ?? Admitted.Everything);
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
            Knowledge known, List<IMistType> considered, IMistType valueType, Reading reading, Expression? measured = null, Admitted? length = null,
            LiteralValue? literal = null, Expression? expression = null)
        {
            return considered.Any(constituent => LiteralPredicates.Applies(constituent)
                ? LiteralPredicates.Satisfied(constituent, valueType, literal, expression, matchLimit)
                    || (LiteralPredicates.Decide(constituent, literal, matchLimit) is null && RealKnowledge.Satisfied(known.Real, constituent))
                : Requirement.OfType(constituent) is { } required
                ? Satisfied(known, Read(required, proving: true, reading), measured, length)
                : !Requirement.IsUnreadable(constituent) || ReferenceEquals(Requirement.Refined(valueType), Requirement.Refined(constituent)));
        }

        // A bound naming a member is read through that member's type or lift, which every instance shares:
        // to prove, against the end of the member's interval every value it may take clears; to report,
        // only once the member is one value, so a value it merely might exceed stays unproven.
        private Requirement Read(Requirement required, bool proving, Reading reading)
        {
            var values = required.Values;
            var bounds = ImmutableArray.CreateBuilder<SymbolicBound>();

            foreach(var bound in required.Bounds)
            {
                if(reading.Of(bound, this) is { } read && Limit(bound.Kind, read, proving) is { } limit)
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
            string? onConstraint = null, Expression? onIndex = null, Expression? measured = null, Reading? reading = null)
        {
            if(value.Type is ErrorType)
            {
                return;
            }

            var known = Known(state, value);
            reading ??= Reading.None;

            if(!Satisfied(known, Read(required, proving: true, reading), onIndex ?? measured, Length(state, measured)))
            {
                RefinementFindings.Report(
                    context, known.Values, value, Stated(Read(required, proving: false, reading), Length(state, measured)),
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

                if(bound.Named is TypeParameter { IsDependent: true } dependent && Demanded(dependent) is { } own
                    && Limit(bound.Kind, own.Values, proving: true) is { } floor && known.Values.IsSubsetOf(floor))
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

        // A member bound written through a local is read only when that local is the callee's own extension
        // receiver: a captured receiver or another parameter is not the instance the call supplies, however
        // it is named. A bound naming a sibling parameter reads what the call passes in that position.
        private sealed class Reading(LocalVariable? receiver, Func<Parameter, Admitted?>? sibling)
        {
            public static readonly Reading None = new(null, null);

            public static Reading Of(
                ImmutableDictionary<object, Knowledge> state, IReadOnlyList<Expression> arguments, Method? callee, bool flattened, Proving proving)
            {
                if(callee is null)
                {
                    return None;
                }

                var parameters = callee.ParametersList;

                return new Reading(callee.DeclaringBlock?.Receiver, flattened ? null : named =>
                {
                    var index = parameters.FindIndex(parameter => ReferenceEquals(parameter, named));

                    return index >= 0 && index < arguments.Count && parameters[index].Modifier.Direction != ByRefDirection.Out
                        ? proving.Known(state, arguments[index]).Values
                        : null;
                });
            }

            public Admitted? Of(SymbolicBound bound, Proving proving)
            {
                return bound switch
                {
                    { Member: { } member, Receiver: null } => proving.Signature(member),
                    { Member: { } member, Receiver: var through } when ReferenceEquals(through, receiver) => proving.Signature(member),
                    { Named: Parameter named } => sibling?.Invoke(named),
                    _ => null,
                };
            }
        }
    }
}