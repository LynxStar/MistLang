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
/// What a dependent slot read through a value is, <c>a.Length</c> or <c>ring.Capacity</c>: the value the
/// receiver's type was closed with, read from the receiver type's argument for that parameter and never
/// from the type of the read, which is the slot's domain whatever the closing holds; the slot's <c>where</c>
/// is a fact on the value, never part of the type. A literal is its
/// constant, a capture the value its local holds, a dependent parameter the value bound under it, and the
/// <c>unknown</c> slot of a class or a struct the value under the member holder of the receiver, which
/// an object keeps for life (runtime-dependent-values Requirement 7). The slot of an abstract is never
/// keyed by the object (Requirement 11.4): through a named closing it is one of the first three, and
/// through <c>unknown</c> it does not bind.
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
            CapturedValue captured => reading.Read(ref state, new LocalReferenceExpression(captured.Capture.Initializer.Node, captured.Capture)),
            TypeParameter { IsDependent: true } dependent => reading.Symbol(ref state, new SymbolicBound(dependent.Name, ComparisonKind.Equal) { Named = dependent }),
            UnknownType or UnknownValueType when through is not null && parameter.DeclaringType is ClassType or StructType => reading.Read(ref state, Read(through, parameter)),
            _ => null,
        };
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
        return state.Bindings.Get(captured.Capture) is { } value && state.Bag.Intervals(value).Intervals is [var only] && only.Low is { } low && low == only.High
            ? low
            : null;
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

            if(slot is UnknownType or UnknownValueType or ErrorType || ReferenceEquals(argument, slot)
                || (source.Definition is not (ClassType or StructType) && argument is not TypeParameter { IsDependent: true }))
            {
                continue;
            }

            if(Value(ref state, reading, argument, value, own) is { } read && Value(ref state, reading, slot, null, parameter) is { } held)
            {
                state = state with { Bag = state.Bag.Add(new Fact(read.Subtract(held), Relation.Equal)) };
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