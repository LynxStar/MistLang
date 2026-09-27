using System.Collections.Immutable;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// What a refinement predicate asks of a value, read off the pattern the binder bound for it: the
/// integral set it admits, and the bounds it names symbolically -- `&lt; Length` inside Array, which
/// no number stands for until an array is in hand. Null where the predicate has no integral reading;
/// supported real, string and regex constant decisions belong to <see cref="LiteralPredicates"/>.
/// </summary>
internal sealed record Requirement(Admitted Values, ImmutableArray<SymbolicBound> Bounds)
{
    public static readonly Requirement Anything = new(Admitted.Everything, []);

    public static Requirement? Of(Pattern? predicate)
    {
        switch(predicate)
        {
            case null:
                return null;

            case ComparisonPattern comparison when Comparability.Misordered(comparison) is not null:
                return null;

            case ComparisonPattern { Kind: var kind, Bound: var bound }:
                if(Constant(bound) is { } constant)
                {
                    return new Requirement(Interval(kind, constant), []);
                }

                return Symbol(bound) is var (name, member) && kind != ComparisonKind.Equal
                    ? new Requirement(Admitted.Everything, [new SymbolicBound(name, kind, member)])
                    : null;

            case ConjunctionPattern conjunction:
                {
                    var values = Admitted.Everything;
                    var bounds = ImmutableArray.CreateBuilder<SymbolicBound>();

                    foreach(var operand in conjunction.Operands)
                    {
                        if(Of(operand) is not { } part)
                        {
                            return null;
                        }

                        values = values.And(part.Values);
                        bounds.AddRange(part.Bounds);
                    }

                    return new Requirement(values, bounds.ToImmutable());
                }

            case DisjunctionPattern disjunction:
                {
                    var values = Admitted.Nothing;

                    foreach(var operand in disjunction.Operands)
                    {
                        if(Of(operand) is not { Bounds.Length: 0 } part)
                        {
                            return null;
                        }

                        values = values.Or(part.Values);
                    }

                    return new Requirement(values, []);
                }

            case NegatedPattern negated:
                return Of(negated.Operand) is { Bounds.Length: 0 } inner ? new Requirement(inner.Values.Not(), []) : null;

            case TypeTestPattern { Refinement: { } refinement }:
                return Of(refinement);

            case TypeTestPattern { NarrowedType: var narrowed } when OfType(narrowed) is { } named:
                return named;

            case CatchAllPattern:
                return Anything;

            default:
                return null;
        }
    }

    public static Requirement? OfType(IMistType type)
    {
        switch(type)
        {
            case RefinedType refined:
                {
                    var own = Of(refined.Predicate);
                    var beneath = OfType(refined.BaseType);
                    return own is null || (beneath is null && Refined(refined.BaseType) is not null) ? null : beneath is null ? own : own.And(beneath);
                }

            case TypeAlias { AliasedType: { } aliased }:
                return OfType(aliased);

            default:
                return null;
        }
    }

    public static Admitted? ValuesOf(IMistType type)
    {
        return type switch
        {
            LiftedType { Value: long value } => Admitted.Exactly(value),
            LiftedType { Value: int value } => Admitted.Exactly(value),
            LiftedType { Value: char value } => Admitted.Exactly(value),
            LiftedType { Value: bool value } => Admitted.Exactly(value ? 1 : 0),
            _ => OfType(type)?.Values,
        };
    }

    public static IMistType Unrefined(IMistType type)
    {
        return type switch
        {
            RefinedType refined => Unrefined(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => Unrefined(aliased),
            _ => type,
        };
    }

    public static bool IsIntegral(IMistType type)
    {
        return type.FullyQualifiedName is var name
            && (name == WellKnownTypes.Int32.FullyQualifiedName || name == WellKnownTypes.Int64.FullyQualifiedName
                || name == WellKnownTypes.Int16.FullyQualifiedName || name == WellKnownTypes.UInt32.FullyQualifiedName
                || name == WellKnownTypes.UInt64.FullyQualifiedName || name == WellKnownTypes.UInt16.FullyQualifiedName
                || name == WellKnownTypes.Byte.FullyQualifiedName || name == WellKnownTypes.SByte.FullyQualifiedName
                || name == WellKnownTypes.Char.FullyQualifiedName);
    }

    public static bool IsUnreadable(IMistType type)
    {
        return Refined(type) is not null && Stated(type) && OfType(type) is null;
    }

    private static bool Stated(IMistType type)
    {
        return Refined(type) is not RefinedType refined
            || (refined.Predicate is not null && !Comparability.IsRefused(refined) && Stated(refined.BaseType));
    }

    public static bool IsAnonymous(IMistType type)
    {
        return Refined(type) is RefinedType { IsAnonymous: true };
    }

    public static IMistType? Refined(IMistType type)
    {
        return type switch
        {
            RefinedType refined => refined,
            TypeAlias { AliasedType: { } aliased } => Refined(aliased),
            _ => null,
        };
    }

    public static long? Constant(Expression expression)
    {
        return expression switch
        {
            LiteralExpression { Value: long value } => value,
            LiteralExpression { Value: int value } => value,
            LiteralExpression { Value: char value } => value,
            LiteralExpression { Value: bool value } => value ? 1 : 0,
            MemberAccessExpression { Member: EnumMemberResult } access => EnumConstants.Of(access),
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operator: null or { Modifiers.IsIntrinsic: true }, Operand: var operand }
                when Constant(operand) is { } constant && constant != long.MinValue => -constant,
            CloneExpression clone => Constant(clone.Source),
            _ => null,
        };
    }

    public Requirement And(Requirement other)
    {
        return new Requirement(Values.And(other.Values), Bounds.AddRange(other.Bounds));
    }

    public static Admitted Interval(ComparisonKind kind, long constant)
    {
        return kind switch
        {
            ComparisonKind.Equal => Admitted.Exactly(constant),
            ComparisonKind.LessThan => Admitted.Below(constant),
            ComparisonKind.LessThanOrEqual => Admitted.AtMost(constant),
            ComparisonKind.GreaterThan => Admitted.Above(constant),
            _ => Admitted.AtLeast(constant),
        };
    }

    private static (string Name, object? Member)? Symbol(Expression bound)
    {
        return bound switch
        {
            TypeReferenceExpression { Type: TypeParameter parameter } => (parameter.Name, null),
            MemberAccessExpression { Receiver: null or ThisExpression, MemberName: var name, Member: PropertyResult { Property: var property } } => (name, property),
            MemberAccessExpression { Receiver: null or ThisExpression, MemberName: var name, Member: FieldResult { Field: var field } } => (name, field),
            MemberAccessExpression { Receiver: null or ThisExpression, MemberName: var name } => (name, null),
            PropertyReferenceExpression { Property: var property } => (property.Name, property),
            FieldReferenceExpression { Field: var field } => (field.Name, field),
            LocalReferenceExpression local => (local.Local.Name, null),
            _ => null,
        };
    }
}

/// <summary>
/// A bound a predicate names rather than states. <see cref="Member"/> is the property or field it names,
/// when it names one, so a use can read that member's type or lift (refinement-types R18) -- never the
/// flow, which does not cross a member -- and a bound with no member is met only by what the flow
/// relates to it, an array's or a span's `Length`.
/// </summary>
internal sealed record SymbolicBound(string Name, ComparisonKind Kind, object? Member = null);