using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// What a dependent slot read through a value is, <c>a.Length</c> or <c>ring.Capacity</c>, and what a slot
/// argument admits (runtime-dependent-values Requirements 1, 7 and 9). A literal is its constant and a
/// dependent parameter the value bound under it. Any other slot of a class or a struct, a capture, a
/// refinement or <c>unknown</c>, is the value under the member holder of the receiver, which an object
/// keeps for life: a creation records it equal to the value it closed the object with, a copy or a cast
/// carries it, a write to the variable ends it, and the receiver type's refinement is a fact on it. A capture
/// admits the refinement <see cref="Keep"/> fixes at its Declare step, never its value. The slot of an
/// abstract is never keyed by the object (Requirement 11.4).
/// </summary>
internal static class SlotReads
{
    public static TypeParameter? Parameter(MemberAccessExpression access)
    {
        return access is { IsConditional: false, IsFailed: false, Receiver: not TypeReferenceExpression, Member: TypeResult { Type: TypeParameter { IsDependent: true, IsPack: false } parameter } }
            ? parameter
            : null;
    }

    public static object? Member(MemberAccessExpression access)
    {
        return (object?)Parameter(access) ?? (IsStringLength(access) ? "Length" : null);
    }

    public static bool IsLength(MemberAccessExpression access)
    {
        return Lengths.IsSized(access.Receiver.Type) && (Parameter(access) is { Ordinal: 1 } || IsStringLength(access));
    }

    public static MemberAccessExpression? LengthOf(Expression receiver)
    {
        return Requirement.Unrefined(receiver.Type) is ConstructedType { Definition: ConcreteType { TypeParametersList: [_, { IsDependent: true } length, ..] } definition }
            && Lengths.IsIndexable(definition)
                ? Read(receiver, length)
                : null;
    }

    public static MemberAccessExpression Read(Expression receiver, TypeParameter parameter)
    {
        return new MemberAccessExpression(receiver.Node, receiver, parameter.Name, parameter.Domain ?? receiver.Type, member: new TypeResult(parameter));
    }

    public static IMistType? Contract(Expression expression)
    {
        return expression is MemberAccessExpression access && Parameter(access) is { } parameter ? InstanceSlotRead.Contract(parameter) : null;
    }

    public static IMistType? Argument(MemberAccessExpression access, TypeParameter parameter)
    {
        return parameter.DeclaringType is { } definition && Closing(access.Receiver.Type, definition) is { } closing && parameter.Ordinal < closing.TypeArguments.Count
            ? closing.TypeArguments[parameter.Ordinal]
            : null;
    }

    public static ConstructedType? Closing(IMistType source, IMistType definition)
    {
        return Closing(source, definition, []);
    }

    private static ConstructedType? Closing(IMistType source, IMistType definition, HashSet<string> visited)
    {
        var from = Plain(source);

        if(from is ConstructedType constructed && (ReferenceEquals(constructed.Definition, definition) || ConversionRelation.IsIdentity(constructed.Definition, definition)))
        {
            return constructed;
        }

        return ConversionRelation.Bases(from)
            .Where(wider => visited.Add(wider.FullyQualifiedName))
            .Select(wider => Closing(wider, definition, visited))
            .FirstOrDefault(found => found is not null);
    }

    public static Term? Value(ref FactState state, FactReading reading, IMistType argument, Expression? through, TypeParameter parameter)
    {
        return argument switch
        {
            LiftedType lifted when Constant(lifted) is { } constant => new Term(constant),
            CapturedValue captured when through is not null && Creates(through, captured.Capture)
                => reading.Read(ref state, new LocalReferenceExpression(captured.Capture.Initializer.Node, captured.Capture)),
            CapturedValue or UnknownType or UnknownValueType when through is not null && parameter.DeclaringType is ClassType or StructType
                => reading.Read(ref state, Read(through, parameter)),
            CapturedValue captured => reading.Read(ref state, new LocalReferenceExpression(captured.Capture.Initializer.Node, captured.Capture)),
            TypeParameter { IsDependent: true } dependent => reading.Symbol(ref state, new SymbolicBound(dependent.Name, ComparisonKind.Equal) { Named = dependent }),
            ParameterValue held => reading.Symbol(ref state, new SymbolicBound(held.Parameter.Name, ComparisonKind.Equal) { Named = held.Parameter }),
            _ => null,
        };
    }

    private static bool Creates(Expression through, SlotCapture capture)
    {
        return Written(through) is NewExpression or ArrayCreationExpression or StackallocExpression or DefaultExpression or InvocationExpression
            && SlotCapture.Of(Written(through)).Contains(capture);
    }

    public static bool StandsForOneValue(IMistType argument)
    {
        return argument is LiftedType or ParameterValue or TypeParameter { IsDependent: true };
    }

    public static FactState Keep(FactState state, SlotCapture capture)
    {
        return state.Bindings.Get(capture) is { } value && IntegralTypes.Range(capture.Type) is not null
            ? state with { Kept = state.Kept.SetItem(capture, state.Bag.Intervals(value).And(IntegralSet.Of(Typed(capture.Type)))) }
            : state;
    }

    public static Admitted? Refinement(FactState state, IMistType argument)
    {
        return argument switch
        {
            CapturedValue captured when IntegralTypes.Range(captured.Capture.Type) is not null => state.Kept.TryGetValue(captured.Capture, out var kept)
                ? FactState.AdmittedValues(kept)
                : Typed(captured.Capture.Type).And(Typed(Written(captured.Capture.Initializer).Type)),
            RefinementArgument refinement when IntegralTypes.Range(refinement.Domain) is not null
                => Typed(refinement.Refinement).And(refinement.Contract is { } contract ? Typed(contract) : Admitted.Everything).And(Typed(refinement.Domain)),
            LiftedType lifted when Constant(lifted) is { } constant && constant >= long.MinValue && constant <= long.MaxValue => Admitted.Exactly((long)constant),
            _ => null,
        };
    }

    public static Admitted Typed(IMistType type)
    {
        var values = Requirement.ValuesOf(type) ?? Admitted.Everything;

        return IntegralTypes.Range(type) is { } range ? values.And(FactState.AdmittedValues(new IntegralSet([range]))) : values;
    }

    public static FactState Created(FactState state, Expression expression, Term term)
    {
        if(term is not { Constant.IsZero: true, Variables: [var made] } || !term.Multipliers[0].IsOne)
        {
            return state;
        }

        if(expression is BinaryExpression { Operator: { } called } operation && SlotIdentity.Closer(operation) is { } closer
            && state.Results.GetValueOrDefault(closer) is { Constant.IsZero: true, Variables: [var from] } closed && closed.Multipliers[0].IsOne)
        {
            return Returned(state, called.ReturnType, expression.Type, from, made);
        }

        var cast = expression switch
        {
            CastExpression { Operator: null } converted => converted.Operand,
            AsExpression converted => converted.Operand,
            _ => null,
        };

        if(cast is not null)
        {
            return state.Results.GetValueOrDefault(cast) is { Constant.IsZero: true, Variables: [var operand] } read && read.Multipliers[0].IsOne ? Carry(state, operand, made) : state;
        }

        var owned = SlotCapture.Of(expression).Concat(expression is InvocationExpression { Receiver: MemberAccessExpression receiver } ? receiver.Captures : []).ToList();

        if(owned.Count == 0 || Plain(expression.Type) is not ConstructedType { Definition: IHasTypeParameters declared } closing || closing.Definition is not (ClassType or StructType))
        {
            return state;
        }

        foreach(var parameter in declared.TypeParametersList.Where(parameter => parameter is { IsDependent: true, IsPack: false } && parameter.Ordinal < closing.TypeArguments.Count))
        {
            if(closing.TypeArguments[parameter.Ordinal] is CapturedValue captured && owned.Exists(capture => ReferenceEquals(capture, captured.Capture))
                && state.Bindings.Get(captured.Capture) is { } value)
            {
                state = state with { Bindings = state.Bindings.Set(new MemberHolder(made, parameter), value) };
            }
        }

        return state;
    }

    private static FactState Returned(FactState state, IMistType declared, IMistType result, Value from, Value made)
    {
        if(Plain(declared) is not ConstructedType written || Plain(result) is not ConstructedType { Definition: ClassType or StructType, Definition: IHasTypeParameters closing })
        {
            return state;
        }

        foreach(var parameter in closing.TypeParametersList.Where(parameter => parameter is { IsDependent: true, IsPack: false } && parameter.Ordinal < written.TypeArguments.Count))
        {
            if(written.TypeArguments[parameter.Ordinal] is TypeParameter { IsDependent: true } owner && state.Bindings.Get(new MemberHolder(from, owner)) is { } value)
            {
                state = state with { Bindings = state.Bindings.Set(new MemberHolder(made, parameter), value) };
            }
        }

        return state;
    }

    public static bool HasSlots(IMistType type)
    {
        return Plain(type) is ConstructedType { Definition: IHasTypeParameters declared } && declared.TypeParametersList.Exists(parameter => parameter.IsDependent);
    }

    public static FactState Carry(FactState state, Value from, Value to)
    {
        if(ReferenceEquals(from, to))
        {
            return state;
        }

        foreach(var holder in state.Bindings.Order.OfType<MemberHolder>().Where(holder => ReferenceEquals(holder.Receiver, from) && holder.Member is TypeParameter).ToList())
        {
            state = state with { Bindings = state.Bindings.Set(holder with { Receiver = to }, state.Bindings.Values[holder]) };
        }

        return state;
    }

    public static BigInteger? Constant(LiftedType lifted)
    {
        return lifted.Value switch
        {
            long value => value,
            int value => value,
            short value => value,
            sbyte value => value,
            byte value => value,
            ushort value => value,
            uint value => value,
            ulong value => value,
            char value => value,
            bool value => value ? 1 : 0,
            _ => null,
        };
    }

    public static BigInteger? Known(FactState state, CapturedValue captured)
    {
        var values = state.Kept.TryGetValue(captured.Capture, out var kept) ? kept
            : state.Bindings.Get(captured.Capture) is { } value ? state.Bag.Intervals(value) : IntegralSet.Everything;

        return values.Intervals is [var only] && only.Low is { } low && low == only.High ? low : null;
    }

    public static FactState Tested(FactState state, FactReading reading, Expression scrutinee, IMistType tested)
    {
        var value = Written(scrutinee);

        if(Plain(tested) is not ConstructedType { Definition: IHasTypeParameters declared } closing || Closing(value.Type, closing.Definition) is not { } source)
        {
            return state;
        }

        foreach(var parameter in declared.TypeParametersList.Where(parameter => parameter is { IsDependent: true, IsPack: false }))
        {
            if(parameter.Ordinal >= closing.TypeArguments.Count || parameter.Ordinal >= source.TypeArguments.Count)
            {
                continue;
            }

            var argument = source.TypeArguments[parameter.Ordinal];
            var slot = closing.TypeArguments[parameter.Ordinal];
            var own = (source.Definition as IHasTypeParameters)?.TypeParametersList.Find(candidate => candidate.Ordinal == parameter.Ordinal) ?? parameter;

            if((slot is UnknownType or UnknownValueType or ErrorType && slot is not RefinementArgument) || ReferenceEquals(argument, slot)
                || (source.Definition is not (ClassType or StructType) && argument is not TypeParameter { IsDependent: true }))
            {
                continue;
            }

            if(Value(ref state, reading, argument, value, own) is not { } read)
            {
                continue;
            }

            if(StandsForOneValue(slot) && Value(ref state, reading, slot, null, parameter) is { } held)
            {
                state = state with { Bag = state.Bag.Add(new Fact(read.Subtract(held), Relation.Equal)) };
            }
            else if(Refinement(state, slot) is { IsEverything: false } admitted)
            {
                state = state with { Bag = state.Bag.AddAlternatives(ContractReader.Intervals(admitted, read)) };
            }
        }

        return state;
    }

    public static Expression Written(Expression value)
    {
        return value switch
        {
            ImplicitConversionExpression conversion => Written(conversion.Operand),
            CloneExpression clone => Written(clone.Source),
            _ => value,
        };
    }

    public static IMistType Plain(IMistType type)
    {
        return Requirement.Unrefined(type);
    }

    private static bool IsStringLength(MemberAccessExpression access)
    {
        var receiver = Requirement.Unrefined(access.Receiver.Type);

        return access.MemberName == "Length" && receiver.FullyQualifiedName == "Standard.String"
            && access.Member is PropertyResult { Property: { Modifiers.IsIntrinsic: true, DeclaringType: { } host } } && ReferenceEquals(host, receiver);
    }
}