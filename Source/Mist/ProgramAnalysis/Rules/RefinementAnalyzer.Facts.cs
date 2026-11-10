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
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class RefinementAnalyzer
{
    private sealed partial class Proving
    {
        private readonly FactReading factReading = new(lifted);

        private Knowledge Known(FactState state, Expression value)
        {
            if(Requirement.Constant(value) is { } constant)
            {
                return new Knowledge(Admitted.Exactly(constant), ImmutableHashSet<Bound>.Empty);
            }

            if(IntegralTypes.Range(value.Type) is not null)
            {
                var term = factReading.Read(ref state, value);
                Value result;
                if(term is { Constant.IsZero: true, Variables.Length: 1 } && term.Multipliers[0].IsOne)
                {
                    result = term.Variables[0];
                }
                else
                {
                    result = FactReading.Fresh(ref state, value.Type, RefinementFindings.Display(value), value.Node);
                    state = state with { Bag = state.Bag.Add(new Fact(new Term(result).Subtract(term), Relation.Equal)) };
                }

                var bounds = value is LocalReferenceExpression read && state.TryGetValue(read.Local, out var held)
                    ? held.Bounds.SelectMany(bound => state.Bindings.Values.Where(pair => ReferenceEquals(pair.Value, bound.Holder))
                        .Select(pair => new Bound(pair.Key, bound.Kind))).ToImmutableHashSet()
                    : ImmutableHashSet<Bound>.Empty;
                return new Knowledge(FactState.AdmittedValues(state.Intervals(result)), bounds);
            }

            var real = RealRequirement.KindOf(value.Type) is not null;
            var typed = real ? Admitted.Everything : (Requirement.ValuesOf(value.Type) ?? Admitted.Everything).And(lifted.Of(value) ?? Admitted.Everything);

            if(value is not LocalReferenceExpression local)
            {
                return new Knowledge(typed, ImmutableHashSet<Bound>.Empty) { Real = RealKnowledge.Of(state, value) };
            }

            var flow = state.TryGetValue(local.Local, out var known) ? known : Knowledge.Unknown;

            return new Knowledge(real ? typed : flow.Values.And(typed), flow.Bounds) { Real = RealKnowledge.Of(state, value) };
        }

        private Expression CompoundValue(ref FactState state, CompoundAssignmentExpression compound)
        {
            var result = (BinaryExpression)CompoundAssignments.LongForm(compound).Value;
            var old = state.CompoundInputs.GetValueOrDefault(compound) ?? factReading.Read(ref state, compound.Target);
            var term = FactReading.Operation(ref state, result, old, factReading.Evaluated(ref state, compound.Value));
            state = state with { Results = state.Results.SetItem(result, term) };
            return result;
        }

        private FactState Prepare(FactState state, FlowGraph graph, IReadOnlyList<Parameter> parameters)
        {
            var steps = graph.Blocks.SelectMany(block => block.Steps).ToList();
            var declared = steps.Select(step => step.Node is LocalDeclarationStatement declaration ? declaration.Local : step.Variable).OfType<LocalVariable>().ToHashSet();
            var reads = steps.Select(step => step.Node).OfType<LocalReferenceExpression>().Where(read => !declared.Contains(read.Local));
            foreach(var read in reads)
            {
                _ = factReading.Read(ref state, read);
            }

            foreach(var parameter in parameters.Where(parameter => (Requirement.OfType(parameter.Type) ?? Requirement.OfReal(parameter.Type)) is { Bounds.Length: > 0 }))
            {
                _ = factReading.Symbol(ref state, new SymbolicBound(parameter.Name, ComparisonKind.Equal) { Named = parameter });
            }

            foreach(var test in graph.Blocks.SelectMany(block => block.Steps).Select(step => step.Node).OfType<IsPatternExpression>())
            {
                bind(test.Pattern, test.Scrutinee);
            }

            return state;

            void bind(Pattern pattern, Expression scrutinee)
            {
                if(pattern.Binding is { } binding)
                {
                    state = state with { PatternSources = state.PatternSources.SetItem(binding, scrutinee) };
                }

                if(pattern is TypeTestPattern { Refinement: { } refinement })
                {
                    bind(refinement, scrutinee);
                }
            }
        }

        private Proof? Proves(FactState state, Expression expression, Requirement required, Reading reading, Expression? measured, IMistType? target = null)
        {
            if(RealRequirement.KindOf(expression.Type) is { } real)
            {
                return ProvesReal(state, expression, required, real, target is null ? required.Values.IsEverything ? RealSet.Everything(real) : null : RealRequirement.Literal(target),
                    reading, measured);
            }

            if(IntegralTypes.Range(expression.Type) is null)
            {
                return null;
            }

            if(required.Values.IsEverything && required.Bounds.IsEmpty && Requirement.Unrefined(expression.Type) is EnumType
                && Known(state, expression).Values.IsEverything)
            {
                return null;
            }

            var term = factReading.Read(ref state, expression);
            var denial = ContractReader.Denial(required, term, bound => ReadSymbol(ref state, bound, expression, reading, measured));
            var contractValues = term.Variables.Concat(denial?.SelectMany(alternative => alternative.Facts).SelectMany(fact => fact.Term.Variables) ?? []).ToHashSet();
            return denial is null ? null : Chain.Prove(state.Bag, denial, contractValues, budget);
        }

        private Term? ReadSymbol(ref FactState state, SymbolicBound bound, Expression expression, Reading reading, Expression? measured)
        {
            if(bound.Operand is MemberAccessExpression measuredMember)
            {
                return bound.Named is Parameter owner && reading.Argument(owner) is { } supplied
                    ? factReading.Read(ref state, FactReading.Rebased(measuredMember, supplied))
                    : factReading.Symbol(ref state, bound);
            }

            if(bound.Named is Parameter parameter && reading.Argument(parameter) is { } argument)
            {
                return factReading.Read(ref state, argument);
            }

            if(bound is { Member: { } through, Receiver.Parameter: { } receiverParameter } && reading.Argument(receiverParameter) is { } receiverArgument)
            {
                return factReading.Read(ref state, FactReading.Through(receiverArgument, through));
            }

            if(bound is { Member: not null, Receiver: not null } && ReferenceEquals(reading, Reading.None))
            {
                return factReading.Symbol(ref state, bound);
            }

            if(bound is { Name: "Length", Member: null } && measured is not null && IntegralTypes.Range(expression.Type) is not null)
            {
                var source = factReading.Read(ref state, measured);
                if(source.Variables is [var receiver])
                {
                    var holder = new MemberHolder(receiver, FactReading.LengthMember(measured.Type));
                    if(state.Bindings.Get(holder) is not { } length)
                    {
                        var value = FactReading.Fresh(ref state, expression.Type, $"{RefinementFindings.Display(measured)}.Length", measured.Node);
                        state = state with
                        {
                            Bindings = state.Bindings.Set(holder, value),
                            Bag = FactReading.AddValues(state.Bag, value, FactFlow.Length(state, measured, lifted) ?? Admitted.AtLeast(0)),
                        };
                        length = value;
                    }

                    return new Term(length);
                }
            }

            if(bound.Member is not null && reading.Member(bound, expression.Node) is { } member)
            {
                return factReading.Read(ref state, member);
            }

            if(bound.Member is null && factReading.Symbol(ref state, bound) is { } symbol)
            {
                return symbol;
            }

            if(IntegralTypes.Range(expression.Type) is not null && reading.Of(bound, this) is { } values && !values.IsEverything)
            {
                var value = FactReading.Fresh(ref state, expression.Type, bound.Name, expression.Node);
                state = state with { Bag = FactReading.AddValues(state.Bag, value, values) };
                return new Term(value);
            }

            return null;
        }

        private Proof? ProvesReal(FactState state, Expression expression, Requirement contract, RealKind kind, RealSet? literal, Reading reading, Expression? measured)
        {
            if(literal is null || literal.Kind != kind)
            {
                return null;
            }

            var term = factReading.Read(ref state, expression);
            if(RealFacts.Denial(literal, term) is not { } denial)
            {
                return null;
            }

            var alternatives = denial.ToList();
            foreach(var bound in contract.Bounds)
            {
                if(bound.Operand is not null || ReadSymbol(ref state, bound, expression, reading, measured) is not { } limit
                    || RealFacts.Bound(kind, term, bound.Kind, limit) is not { } fact)
                {
                    return null;
                }

                alternatives.AddRange(fact.Denial());
            }

            var bag = state.Bag;
            if((!literal.IncludesNaN || contract.Bounds.Length > 0) && kind != RealKind.Decimal && RealKnowledge.Of(state, expression) is not { IncludesNaN: false }
                && !term.Variables.All(value => RealFacts.Ordered(bag, value)))
            {
                return null;
            }

            var values = term.Variables.Concat(alternatives.SelectMany(alternative => alternative.Facts).SelectMany(fact => fact.Term.Variables)).ToHashSet();
            return Chain.Prove(bag, alternatives, values, budget);
        }

        private Proof? ProvesMatch(FactState state, Expression expression, IMistType target)
        {
            if(Requirement.Unrefined(expression.Type).FullyQualifiedName != WellKnownTypes.String.FullyQualifiedName
                || Requirement.Unrefined(target).FullyQualifiedName != WellKnownTypes.String.FullyQualifiedName
                || factReading.Read(ref state, expression) is not { Constant.IsZero: true, Variables: [var text] } term || !term.Multipliers[0].IsOne)
            {
                return null;
            }

            var details = state.Details;
            return Matching.Proven(state.Bag, text, target, value => details.GetValueOrDefault(value)?.Literal,
                value => details.GetValueOrDefault(value)?.Matches ?? [], matchLimit) ? new Proven() : null;
        }

        // A member bound written through a local is read only when that local is the callee's own extension
        // receiver or one of its parameters, whose member is read on the argument the call passes for it: a
        // captured receiver is not the instance the call supplies, however it is named. A bound naming a
        // sibling parameter reads what the call passes in that position.
        private sealed class Reading(LocalVariable? receiver, Func<Parameter, Admitted?>? sibling, Func<Parameter, Expression?>? argument = null, Expression? instance = null, bool currentInstance = true)
        {
            public Expression? Argument(Parameter parameter)
            {
                return argument?.Invoke(parameter);
            }

            public static readonly Reading None = new(null, null);

            public static Reading ForReceiver(Expression instance)
            {
                return new Reading(null, null, instance: instance);
            }

            public Expression? Member(SymbolicBound bound, Storm.Runtime.SyntaxNode node)
            {
                if(bound.Receiver is not null && !ReferenceEquals(bound.Receiver, receiver))
                {
                    return null;
                }

                return bound.Member switch
                {
                    Field field when field.Modifiers.IsStatic || (instance is null && currentInstance) => new FieldReferenceExpression(node, field),
                    Property property when property.Modifiers.IsStatic || (instance is null && currentInstance) => new PropertyReferenceExpression(node, property),
                    Field field when instance is not null => new MemberAccessExpression(node, instance, field.Name, field.Type,
                        member: new FieldResult(field, field.DeclaringType ?? instance.Type, [])),
                    Property property when instance is not null => new MemberAccessExpression(node, instance, property.Name, property.Type,
                        member: new PropertyResult(property, property.DeclaringType ?? instance.Type, [])),
                    _ => null,
                };
            }

            public static Reading Of(
                FactState state, IReadOnlyList<Expression> arguments, Method? callee, bool flattened, Proving proving, Expression? instance = null)
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
                }, flattened ? null : named =>
                {
                    var index = parameters.FindIndex(parameter => ReferenceEquals(parameter, named));
                    return index >= 0 && index < arguments.Count && parameters[index].Modifier.Direction != ByRefDirection.Out ? arguments[index] : null;
                }, instance, currentInstance: callee is not Constructor);
            }

            public Admitted? Of(SymbolicBound bound, Proving proving)
            {
                return bound switch
                {
                    { Operand: not null } => null,
                    { Member: { } member, Receiver: null } => proving.Signature(member),
                    { Member: { } member, Receiver: var through } when ReferenceEquals(through, receiver) => proving.Signature(member),
                    { Member: { } member, Receiver.Parameter: { } owner } when Argument(owner) is not null => proving.Signature(member),
                    { Named: Parameter named } => sibling?.Invoke(named),
                    _ => null,
                };
            }
        }
    }
}