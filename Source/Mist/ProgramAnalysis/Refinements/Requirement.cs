using System.Collections.Immutable;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// What a refinement predicate asks of a value, read off the pattern the binder bound for it: the
/// integral set it admits, and the bounds it names symbolically -- `&lt; Length` inside Array, which
/// no number stands for until an array is in hand. Null where the predicate says something this
/// prover cannot decide, a property pattern or a regex, so that nothing is refused on its account.
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

            case ComparisonPattern { Kind: var kind, Bound: var bound }:
                if(Constant(bound) is { } constant)
                {
                    return new Requirement(Interval(kind, constant), []);
                }

                return Symbol(bound) is { } name && kind != ComparisonKind.Equal
                    ? new Requirement(Admitted.Everything, [new SymbolicBound(name, kind)])
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
        return type switch
        {
            RefinedType refined => Of(refined.Predicate),
            TypeAlias { AliasedType: { } aliased } => OfType(aliased),
            _ => null,
        };
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
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: var operand } => -Constant(operand),
            CloneExpression clone => Constant(clone.Source),
            _ => null,
        };
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

    private static string? Symbol(Expression bound)
    {
        return bound switch
        {
            TypeReferenceExpression { Type: TypeParameter parameter } => parameter.Name,
            MemberAccessExpression { Receiver: null or ThisExpression, MemberName: var name } => name,
            LocalReferenceExpression local => local.Local.Name,
            _ => null,
        };
    }
}

internal sealed record SymbolicBound(string Name, ComparisonKind Kind);