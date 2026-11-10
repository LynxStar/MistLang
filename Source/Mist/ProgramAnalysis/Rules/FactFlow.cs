using System.Collections.Immutable;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class FactFlow(IReadOnlySet<object> unseenWrites, LiftedReturns lifted, FieldEffects? effects = null, FlowGraph? graph = null, int alternativeCap = Bag.AlternativeCap)
    : IFlowLattice<FactState>
{
    private readonly FieldEffects effects = effects ?? new();
    private readonly FactReading reading = new(lifted);
    private readonly Dictionary<object, List<CompoundAssignmentExpression>> compoundReads = CompoundReads(graph);
    private readonly Dictionary<FlowBlock, LoopChanges> loopChanges = LoopChanges.Of(graph);
    public static readonly FactState Nothing = FactState.Empty;

    public FactState Transfer(FactState state, FlowStep step)
    {
        state = state with { Unseen = unseenWrites, Bag = state.Bag.WithCap(alternativeCap), WideningEntry = null, WideningHeader = null };
        if(step.Kind == FlowStepKind.Evaluate && compoundReads.TryGetValue(step.Node, out var compounds))
        {
            foreach(var compound in compounds)
            {
                if(!state.CompoundInputs.ContainsKey(compound))
                {
                    var before = reading.Read(ref state, compound.Target);
                    state = state with { CompoundInputs = state.CompoundInputs.SetItem(compound, before) };
                }
            }
        }

        switch(step)
        {
            case { Kind: FlowStepKind.Assume, Node: Expression condition }:
                return Learn(state, condition, step.Holds);

            case { Kind: FlowStepKind.Bind, Variable: { } bound } when state.PatternSources.TryGetValue(bound, out var scrutinee):
                return Give(state, bound, scrutinee, matched: true);

            case { Kind: FlowStepKind.Introduce or FlowStepKind.Bind, Variable: { } local }:
                return Give(state, local, null);

            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: null } declaration }:
                return state with { Bindings = state.Bindings.Remove([declaration.Local]) };

            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration }:
                return Give(state, declaration.Local, declaration.Initializer);

            case { Kind: FlowStepKind.Leave, Node: ForStatement { Initializer: LocalDeclarationStatement initializer } }:
                return state with { Bindings = state.Bindings.Remove([initializer.Local]) };

            case { Kind: FlowStepKind.Leave, Node: Block block }:
                return state with { Bindings = state.Bindings.Remove(block.Statements.OfType<LocalDeclarationStatement>().Select(declaration => (object)declaration.Local)) };

            case { Kind: FlowStepKind.Evaluate, Node: CompoundAssignmentExpression compound }:
                var longForm = CompoundAssignments.LongForm(compound);
                var old = state.CompoundInputs.GetValueOrDefault(compound) ?? reading.Read(ref state, compound.Target);
                var result = FactReading.Operation(ref state, (BinaryExpression)longForm.Value, old, reading.Evaluated(ref state, compound.Value));
                state = state with { Results = state.Results.SetItem(longForm.Value, result), CompoundInputs = state.CompoundInputs.Remove(compound) };
                if(effects.MayWrite(compound))
                {
                    state = InvalidateFields(state);
                }

                return Assign(state, longForm);

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression assignment }:
                state = Assign(state, assignment);
                return effects.MayWrite(assignment) ? InvalidateFields(state) : state;

            case { Kind: FlowStepKind.Evaluate, Node: MethodReferenceExpression or NamespaceReferenceExpression }:
                return state;

            case { Kind: FlowStepKind.Evaluate, Node: Expression expression }:
                state = state with { Results = state.Results.Remove(expression) };
                if(effects.MayWrite(expression))
                {
                    state = InvalidateFields(state);
                }

                state = InvalidateArguments(state, expression);
                var term = reading.Read(ref state, expression);
                return state with { Results = state.Results.SetItem(expression, term) };

            default:
                return state;
        }
    }

    private static Dictionary<object, List<CompoundAssignmentExpression>> CompoundReads(FlowGraph? graph)
    {
        var reads = new Dictionary<object, List<CompoundAssignmentExpression>>(ReferenceEqualityComparer.Instance);
        foreach(var compound in graph?.Blocks.SelectMany(block => block.Steps).Select(step => step.Node).OfType<CompoundAssignmentExpression>() ?? [])
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(compound.Value))
            {
                if(!reads.TryGetValue(node, out var list))
                {
                    list = [];
                    reads.Add(node, list);
                }

                list.Add(compound);
            }
        }

        return reads;
    }

    private FactState Give(FactState state, LocalVariable local, Expression? expression, bool matched = false)
    {
        var term = expression is not null ? reading.Read(ref state, expression) : null;
        var known = expression is not null ? KnowledgeOf(state, expression, local.Type) : Knowledge.Unknown;
        var trusted = matched || term is null || AssignmentHolds(ref state, local.Type, term);
        var supplied = expression is null && local.Parameter is { } parameter ? state.Bindings.Get(parameter) : null;
        var value = supplied ?? Shared(local.Type, expression, term) ?? FactReading.Fresh(ref state, local.Type, local.Name, local.Parameter?.DeclarationNode!, trusted);
        state = state with { Bindings = state.Bindings.Set(local, value) };
        if(term is not null && Shared(local.Type, expression, term) is null)
        {
            state = state with { Bag = state.Bag.Add(new Fact(new Term(value).Subtract(term), Relation.Equal)) };
        }

        if(trusted)
        {
            reading.AddSymbols(ref state, value, local.Type);
        }

        return RememberBoolean(state.SetItem(local, known), value, expression);
    }

    private FactState Assign(FactState state, AssignmentExpression assignment)
    {
        if(assignment.Target is ThisExpression)
        {
            return InvalidateFields(state, all: true);
        }

        var term = reading.Read(ref state, assignment.Value);
        var known = KnowledgeOf(state, assignment.Value, assignment.Target.Type);
        if(reading.Holder(ref state, assignment.Target) is not { } holder)
        {
            return state;
        }

        var trusted = AssignmentHolds(ref state, assignment.Target.Type, term);
        var value = Shared(assignment.Target.Type, assignment.Value, term)
            ?? FactReading.Fresh(ref state, assignment.Target.Type, RefinementFindings.Display(assignment.Target), assignment.Node, trusted);
        state = state with { Bindings = state.Bindings.Set(holder, value), StableHolders = state.StableHolders.Remove(holder), Results = state.Results.SetItem(assignment, new Term(value)) };
        if(!assignment.IsCoalescing)
        {
            state = state with { Bag = state.Bag.Add(new Fact(new Term(value).Subtract(term), Relation.Equal)) };
        }

        if(trusted)
        {
            reading.AddSymbols(ref state, value, assignment.Target.Type);
        }

        return RememberBoolean(state.SetItem(holder, known), value, assignment.Value);
    }

    private FactState RememberBoolean(FactState state, Value value, Expression? expression)
    {
        if(expression is not BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } comparison
            || Comparison(comparison.OperatorKind) is not { } kind || Compared(ref state, comparison, kind) is not { } compared)
        {
            return state;
        }

        var denial = compared.Deniable ? compared.Fact.Denial() : [Alternative.Empty];
        return state with { Bag = state.Bag.AddAlternatives(Booleans.Initializer(value, compared.Fact, comparison.OperatorKind != BinaryOperatorKind.NotEqual, denial)) };
    }

    // A failed comparison of binary reals may have met a NaN, which no fact over the order can say, so it states nothing.
    private (Fact Fact, bool Deniable)? Compared(ref FactState state, BinaryExpression comparison, ComparisonKind kind)
    {
        var left = comparison.Left.Type;
        var right = comparison.Right.Type;
        if(Countable(left) && Countable(right))
        {
            return (ContractReader.Compare(reading.Evaluated(ref state, comparison.Left), kind, reading.Evaluated(ref state, comparison.Right)), true);
        }

        if(RealRequirement.KindOf(left) is { } mine && RealRequirement.KindOf(right) is { } theirs)
        {
            return (RealFacts.Compare(reading.Evaluated(ref state, comparison.Left), kind, reading.Evaluated(ref state, comparison.Right)),
                mine == RealKind.Decimal && theirs == RealKind.Decimal);
        }

        return kind == ComparisonKind.Equal && IsString(left) && IsString(right)
            ? (ContractReader.Compare(reading.Evaluated(ref state, comparison.Left), kind, reading.Evaluated(ref state, comparison.Right)), false)
            : null;
    }

    private FactState Spelled(FactState state, Expression text, Expression literal)
    {
        if(LiteralValue.Of(literal) is not { Value: string } spelled || reading.Evaluated(ref state, text) is not { Constant.IsZero: true, Variables: [var value] } term
            || !term.Multipliers[0].IsOne)
        {
            return state;
        }

        return state with { Details = state.Details.SetItem(value, (state.Details.GetValueOrDefault(value) ?? Knowledge.Unknown) with { Literal = spelled }) };
    }

    private static bool Countable(IMistType type)
    {
        return IntegralTypes.Range(type) is not null || Requirement.Unrefined(type).FullyQualifiedName == WellKnownTypes.Boolean.FullyQualifiedName;
    }

    private static bool IsString(IMistType type)
    {
        return Requirement.Unrefined(type).FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName;
    }

    private bool AssignmentHolds(ref FactState state, IMistType target, Term term)
    {
        if((IntegralTypes.Range(target) is null && Requirement.Unrefined(target).FullyQualifiedName != "Standard.Boolean") || Requirement.OfType(target) is not { } required)
        {
            return true;
        }

        var current = state;
        var denial = ContractReader.Denial(required, term, bound => reading.Symbol(ref current, bound));
        state = current;
        return denial is not null && Chain.Prove(state.Bag, denial, term.Variables.ToHashSet()) is Proven;
    }

    private static Value? Shared(IMistType type, Expression? expression, Term? term)
    {
        var plain = Requirement.Unrefined(type);
        var reference = plain is ClassType or AbstractType or ConstructedType { Definition: ClassType or AbstractType } or IFunctionType;
        if(!reference || IntegralTypes.Range(plain) is not null || RealRequirement.KindOf(plain) is not null
            || plain.FullyQualifiedName is "Standard.String" or "Standard.Boolean" or "Standard.Regex")
        {
            return null;
        }

        return expression is LocalReferenceExpression or FieldReferenceExpression or MemberAccessExpression
            && term is { Constant.IsZero: true, Variables.Length: 1 } && term.Multipliers[0].IsOne
            ? term.Variables[0] : null;
    }

    private Knowledge KnowledgeOf(FactState state, Expression expression, IMistType target)
    {
        var copied = Holder(expression) is { } holder && state.TryGetValue(holder, out var held) ? held : Knowledge.Unknown;
        var real = RealKnowledge.Assigned(state, expression, target);
        var literal = expression is RegexLiteralExpression regex ? new LiteralValue(regex.Pattern, WellKnownTypes.Regex.FullyQualifiedName)
            : LiteralValue.Of(expression, local => state.TryGetValue(local, out var known) ? known.Literal : null)?.ConvertedTo(target);
        return copied with
        {
            Literal = literal,
            Real = real,
            Length = Length(state, expression, lifted),
            Slice = Range(state, expression, lifted),
            IndexValue = RangeBound.IsIndex(target) ? RangeKnowledge.Index(state, expression, lifted) : null,
            RefusedSlice = Refused(state, expression, lifted),
        };
    }

    private static FactState InvalidateFields(FactState state, bool all = false)
    {
        foreach(var holder in state.Bindings.Order)
        {
            if(holder is Field field && (all || !field.Modifiers.IsReadonly))
            {
                var value = FactReading.Fresh(ref state, field.Type, field.Name, state.Bindings.Values[holder].Node);
                state = state with { Bindings = state.Bindings.Set(holder, value) };
            }
        }

        return state;
    }

    private FactState InvalidateArguments(FactState state, Expression expression)
    {
        var (arguments, parameters) = expression switch
        {
            InvocationExpression call => (call.Arguments, call.Parameters),
            NewExpression creation => (creation.Arguments, creation.Constructor?.ParametersList),
            _ => (null, null),
        };
        for(var i = 0; arguments is not null && parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out && reading.Holder(ref state, arguments[i]) is { } holder)
            {
                var value = FactReading.Fresh(ref state, arguments[i].Type, RefinementFindings.Display(arguments[i]), arguments[i].Node);
                state = state with { Bindings = state.Bindings.Set(holder, value) };
            }
        }

        return state;
    }

    private FactState Learn(FactState state, Expression condition, bool holds)
    {
        if(condition is BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } comparison && Comparison(comparison.OperatorKind) is { } kind)
        {
            var positive = comparison.OperatorKind == BinaryOperatorKind.NotEqual ? !holds : holds;
            if(Compared(ref state, comparison, kind) is { } compared)
            {
                state = state with
                {
                    Bag = positive ? state.Bag.Add(compared.Fact) : compared.Deniable ? state.Bag.AddAlternatives(compared.Fact.Denial()) : state.Bag,
                };
                if(positive && kind == ComparisonKind.Equal && IsString(comparison.Left.Type))
                {
                    state = Spelled(Spelled(state, comparison.Left, comparison.Right), comparison.Right, comparison.Left);
                }
            }

            if(RealRequirement.KindOf(comparison.Left.Type) is not null || RealRequirement.KindOf(comparison.Right.Type) is not null)
            {
                if(comparison.Left is LocalReferenceExpression local)
                {
                    return RealKnowledge.Compared(state, local.Local, comparison.Right, kind, positive);
                }

                return comparison.Right is LocalReferenceExpression mirrored
                    ? RealKnowledge.Compared(state, mirrored.Local, comparison.Left, Mirrored(kind), positive) : state;
            }

            if(IntegralTypes.Range(comparison.Left.Type) is not null && IntegralTypes.Range(comparison.Right.Type) is not null)
            {
                state = RememberLength(state, comparison.Left, comparison.Right, positive ? kind : Flipped(kind));
                state = RememberLength(state, comparison.Right, comparison.Left, Mirrored(positive ? kind : Flipped(kind)));
            }
        }
        else if(condition is IsPatternExpression test && IntegralTypes.Range(test.Scrutinee.Type) is not null && Requirement.Of(test.Pattern) is { } required)
        {
            var term = reading.Read(ref state, test.Scrutinee);
            var current = state;
            var denial = ContractReader.Denial(required, term, bound => reading.Symbol(ref current, bound));
            state = current;
            if(denial is not null)
            {
                if(!holds)
                {
                    state = state with { Bag = state.Bag.AddAlternatives(denial) };
                }
                else
                {
                    state = state with { Bag = state.Bag.AddAlternatives(ContractReader.Intervals(required.Values, term)) };
                    foreach(var bound in required.Bounds)
                    {
                        if(reading.Symbol(ref state, bound) is { } symbol)
                        {
                            state = state with { Bag = state.Bag.Add(ContractReader.Compare(term, bound.Kind, symbol)) };
                        }
                    }
                }
            }
        }
        else if(condition is IsPatternExpression realTest && RealRequirement.KindOf(realTest.Scrutinee.Type) is { } realKind
            && RealRequirement.Of(realTest.Pattern, realKind) is { } matched)
        {
            if(RealFacts.Within(holds ? matched : matched.Not(), reading.Read(ref state, realTest.Scrutinee)) is { } within)
            {
                state = state with { Bag = within is [var only] ? only.Facts.Aggregate(state.Bag, (bag, fact) => bag.Add(fact)) : state.Bag.AddAlternatives(within) };
            }
        }
        else if(condition is IsPatternExpression textTest && holds && IsString(textTest.Scrutinee.Type) && Matching.Stated(textTest.Pattern) is { Length: > 0 } patterns
            && reading.Read(ref state, textTest.Scrutinee) is { Constant.IsZero: true, Variables: [var text] } read && read.Multipliers[0].IsOne)
        {
            var witness = FactReading.Fresh(ref state, textTest.Scrutinee.Type, RefinementFindings.Display(textTest.Scrutinee), textTest.Node);
            state = state with
            {
                Bag = state.Bag.Add(new Fact(new Term(witness).Subtract(new Term(text)), Relation.Equal)),
                Details = state.Details.SetItem(witness, Knowledge.Unknown with { Matches = [.. patterns] }),
            };
        }
        else if(condition is LocalReferenceExpression { Type.FullyQualifiedName: "Standard.Boolean" } boolean)
        {
            var term = reading.Read(ref state, boolean);
            state = state with { Bag = state.Bag.Add(new Fact(term.Subtract(new Term(holds ? 1 : 0)), Relation.Equal)) };
        }

        return LearnSide(state, condition, holds);
    }

    private FactState RememberLength(FactState state, Expression subject, Expression bound, ComparisonKind kind)
    {
        if(subject is not LocalReferenceExpression local || bound is not MemberAccessExpression access || !RangeKnowledge.IsLength(access)
            || kind is not (ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual))
        {
            return state;
        }

        var term = reading.Read(ref state, access.Receiver);
        if(term.Variables.Length != 1)
        {
            return state;
        }

        var known = state.TryGetValue(local.Local, out var current) ? current : Knowledge.Unknown;
        return state.SetItem(local.Local, known with { Bounds = known.Bounds.Add(new Bound(term.Variables[0], kind, IntegralTypes.Range(bound.Type))) });
    }

}