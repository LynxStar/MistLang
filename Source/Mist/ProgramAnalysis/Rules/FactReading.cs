using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class FactReading(LiftedReturns lifted)
{
    public Term Read(ref FactState state, Expression expression)
    {
        if(state.Results.TryGetValue(expression, out var result) && expression is not LocalReferenceExpression and not FieldReferenceExpression and not MemberAccessExpression)
        {
            return result;
        }

        if(expression is CloneExpression clone)
        {
            return Read(ref state, clone.Source);
        }

        if(expression is ImplicitConversionExpression { Operator: null } conversion && IntegralTypes.Range(conversion.Type) is { } range)
        {
            var operand = Evaluated(ref state, conversion.Operand);
            var temporary = new Value(conversion.Type, "conversion", conversion.Node);
            var bag = AddRange(state.Bag, temporary, range);
            if(Chain.Prove(bag, BoundsDenial(operand, range), operand.Variables.ToHashSet()) is Proven)
            {
                return operand;
            }
        }

        if(expression is UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operator: null or { Modifiers.IsIntrinsic: true } } unary)
        {
            var term = Evaluated(ref state, unary.Operand).Scale(-1);
            if(Exactness.Holds(unary, term, state.Bag))
            {
                return term;
            }
        }
        else if(expression is BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } binary && IntegralTypes.Range(binary.Type) is not null)
        {
            return Operation(ref state, binary, Evaluated(ref state, binary.Left), Evaluated(ref state, binary.Right));
        }
        else if(Terms.Read(expression, _ => null) is { } constant)
        {
            return constant;
        }

        if(Holder(ref state, expression) is { } holder)
        {
            var stable = !state.Unseen.Contains(holder) && expression switch
            {
                MemberAccessExpression access => (access.Receiver is ThisExpression && access.Member is FieldResult)
                    || RangeKnowledge.IsLength(access) || (access.Member is { } member && StableMembers.IsStable(member)),
                PropertyReferenceExpression property => StableMembers.IsStable(new PropertyResult(property.Property, property.Property.DeclaringType!, [])),
                _ => true,
            };
            if(stable && state.Bindings.Get(holder) is { } existing)
            {
                return new Term(existing);
            }

            var parameter = (expression as LocalReferenceExpression)?.Local.Parameter;
            var value = stable && parameter is not null && state.Bindings.Get(parameter) is { } supplied
                ? supplied : Fresh(ref state, expression.Type, RefinementFindings.Display(expression), expression.Node);
            if(parameter is not null && stable)
            {
                state = state with { Bindings = state.Bindings.Set(parameter, value) };
            }

            if(stable)
            {
                state = state with { Bindings = state.Bindings.Set(holder, value) };
            }

            if(expression is MemberAccessExpression access2 && RangeKnowledge.IsLength(access2))
            {
                state = state with { Bag = AddValues(state.Bag, value, FactFlow.Length(state, access2.Receiver, lifted) ?? Admitted.AtLeast(0)) };
            }

            if(lifted.Produced(expression) is { } heldLift)
            {
                state = state with { Bag = AddValues(state.Bag, value, heldLift) };
            }

            AddSymbols(ref state, value, expression.Type, (expression as MemberAccessExpression)?.Receiver);
            return new Term(value);
        }

        var fresh = Fresh(ref state, expression.Type, RefinementFindings.Display(expression), expression.Node);
        if(lifted.Produced(expression) is { } produced)
        {
            state = state with { Bag = AddValues(state.Bag, fresh, produced) };
        }

        var instance = expression is InvocationExpression call ? (call.Receiver as MemberAccessExpression)?.Receiver : null;
        AddSymbols(ref state, fresh, expression.Type, instance);
        return new Term(fresh);
    }

    public Term Evaluated(ref FactState state, Expression expression)
    {
        return state.Results.TryGetValue(expression, out var term) ? term : Read(ref state, expression);
    }

    public static Term Operation(ref FactState state, BinaryExpression operation, Term left, Term right)
    {
        var term = operation.OperatorKind switch
        {
            BinaryOperatorKind.Add => left.Add(right),
            BinaryOperatorKind.Subtract => left.Subtract(right),
            BinaryOperatorKind.Multiply when left.Variables.Length == 0 => right.Scale(left.Constant),
            BinaryOperatorKind.Multiply when right.Variables.Length == 0 => left.Scale(right.Constant),
            _ => null,
        };
        if(term is not null && Exactness.Holds(operation, term, state.Bag))
        {
            return term;
        }

        var value = Fresh(ref state, operation.Type, RefinementFindings.Display(operation), operation.Node);
        return new Term(value);
    }

    public object? Holder(ref FactState state, Expression expression)
    {
        if(expression is MemberAccessExpression { Receiver: not ThisExpression } access)
        {
            var receiver = Read(ref state, access.Receiver);
            if(RangeKnowledge.IsLength(access) && receiver is { Constant.IsZero: true, Variables.Length: 1 })
            {
                return new MemberHolder(receiver.Variables[0], LengthMember(access.Receiver.Type));
            }
        }

        return expression switch
        {
            PropertyReferenceExpression property => property.Property,
            TypeReferenceExpression { ReferencedType: TypeParameter { IsDependent: true } dependent } => dependent,
            _ => Terms.Holder(expression, state.Bindings),
        };
    }

    public static MemberAccessExpression Rebased(MemberAccessExpression member, Expression receiver)
    {
        var found = Requirement.Unrefined(receiver.Type) is ConstructedType { Definition: ConcreteType { TypeParametersList: [_, { IsDependent: true } length, ..] } }
            && member.Member is TypeResult ? new TypeResult(length) : member.Member;
        return new MemberAccessExpression(member.Node, receiver, member.MemberName, member.Type, member: found);
    }

    public static object LengthMember(IMistType type)
    {
        return Requirement.Unrefined(type) is ConstructedType { Definition: ConcreteType { TypeParametersList.Count: > 1 } definition }
            ? definition.TypeParametersList[1] : "Length";
    }

    public static Value Fresh(ref FactState state, IMistType type, string name, SyntaxNode node, bool declared = true)
    {
        var value = new Value(type, name, node);
        var bag = state.Bag;
        if(IntegralTypes.Range(type) is { } range)
        {
            bag = AddRange(bag, value, range);
        }

        if(declared && Requirement.ValuesOf(type) is { } admitted)
        {
            bag = AddValues(bag, value, admitted);
        }

        state = state with { Bag = bag };
        return value;
    }

    public Term? Symbol(ref FactState state, SymbolicBound bound, Expression? instance = null)
    {
        if(bound.Operand is MemberAccessExpression measured)
        {
            return Read(ref state, measured);
        }

        if(bound.Operand is not null)
        {
            var current = state;
            var operand = ContractReader.Limit(bound, symbol => Symbol(ref current, symbol, instance));
            state = current;
            return operand;
        }

        if(bound.Named is LocalVariable local)
        {
            return Read(ref state, new LocalReferenceExpression(local.Parameter?.DeclarationNode!, local));
        }

        if(bound.Named is Parameter parameter)
        {
            if(state.Bindings.Values.Keys.OfType<LocalVariable>().FirstOrDefault(local => ReferenceEquals(local.Parameter, parameter)) is { } found)
            {
                return Read(ref state, new LocalReferenceExpression(found.Parameter?.DeclarationNode!, found));
            }

            if(state.Bindings.Get(parameter) is not { } supplied)
            {
                supplied = Fresh(ref state, parameter.Type, parameter.Name, parameter.DeclarationNode!);
                state = state with { Bindings = state.Bindings.Set(parameter, supplied) };
                AddSymbols(ref state, supplied, parameter.Type);
            }

            return new Term(supplied);
        }

        if(bound.Member is Field field && bound.Receiver is null)
        {
            Expression member = instance is null || field.Modifiers.IsStatic ? new FieldReferenceExpression(field.DeclarationNode!, field)
                : new MemberAccessExpression(instance.Node, instance, field.Name, field.Type, member: new FieldResult(field, field.DeclaringType ?? instance.Type, []));
            return Read(ref state, member);
        }

        if(bound.Named is TypeParameter { IsDependent: true } dependent && dependent.Domain is { } domain)
        {
            if(state.Bindings.Get(dependent) is not { } value)
            {
                value = Fresh(ref state, domain, dependent.Name, null!);
                state = state with { Bindings = state.Bindings.Set(dependent, value) };
                if(Requirement.Of(dependent.Refinement) is { } predicate)
                {
                    state = state with { Bag = AddValues(state.Bag, value, predicate.Values) };
                    foreach(var constraint in predicate.Bounds)
                    {
                        if(Symbol(ref state, constraint) is { } term)
                        {
                            state = state with { Bag = state.Bag.Add(ContractReader.Compare(new Term(value), constraint.Kind, term)) };
                        }
                    }
                }
            }

            return new Term(value);
        }

        return null;
    }

    public void AddSymbols(ref FactState state, Value value, IMistType type, Expression? instance = null)
    {
        if(Requirement.OfType(type) is not { } required)
        {
            return;
        }

        foreach(var bound in required.Bounds)
        {
            if(Symbol(ref state, bound, instance) is { } term)
            {
                state = state with { Bag = state.Bag.Add(ContractReader.Compare(new Term(value), bound.Kind, term)) };
            }
        }
    }

    public static Bag AddValues(Bag bag, Value value, Admitted values)
    {
        return values.IsEverything ? bag : bag.Add(Fact.In(value, IntegralSet.Of(values)));
    }

    private static Bag AddRange(Bag bag, Value value, IntegralInterval range)
    {
        return bag.Add(Fact.In(value, new IntegralSet([range])));
    }

    private static IReadOnlyList<Alternative> BoundsDenial(Term term, IntegralInterval range)
    {
        var result = new List<Alternative>();
        if(range.Low is { } low)
        {
            result.Add(new Alternative([new Fact(term.Add(new Term(1 - low)))]));
        }

        if(range.High is { } high)
        {
            result.Add(new Alternative([new Fact(term.Scale(-1).Add(new Term(high + 1)))]));
        }

        return result;
    }
}