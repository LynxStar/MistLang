using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

// A failed reading is unknown: an unsupported conjunct must never disappear from a requested proof.
internal static class RealRequirement
{
    public static RealKind? KindOf(IMistType type)
    {
        return type is LiftedType lifted ? KindOf(lifted.Type) : KindOfName(Requirement.Unrefined(type).FullyQualifiedName);
    }

    public static RealKind? KindOfName(string name)
    {
        return name == WellKnownTypes.Single.FullyQualifiedName ? RealKind.Single
            : name == WellKnownTypes.Double.FullyQualifiedName ? RealKind.Double
            : name == WellKnownTypes.Decimal.FullyQualifiedName ? RealKind.Decimal : null;
    }

    public static RealSet? OfType(IMistType type)
    {
        if(KindOf(type) is not { } kind)
        {
            return null;
        }

        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => OfType(aliased),
            RefinedType refined when !Comparability.IsRefused(refined) =>
                Of(refined.Predicate, kind) is { } own && OfType(refined.BaseType) is { } beneath ? own.And(beneath) : null,
            RefinedType => null,
            LiftedType lifted => RealSet.Exactly(kind, new LiteralValue(lifted.Value, lifted.Type.FullyQualifiedName)),
            _ => RealSet.Everything(kind),
        };
    }

    public static RealSet? Of(Pattern? predicate, RealKind kind)
    {
        switch(predicate)
        {
            case ComparisonPattern comparison when Comparability.Misordered(comparison) is null:
                return LiteralOf(comparison.Bound) is { } bound && IsFinite(bound) ? RealSet.Compare(kind, comparison.Kind, bound) : null;

            case ConjunctionPattern conjunction:
                return Combined(conjunction.Operands, kind, true);

            case DisjunctionPattern disjunction:
                return Combined(disjunction.Operands, kind, false);

            case NegatedPattern negated:
                return Of(negated.Operand, kind)?.Not();

            case TypeTestPattern typed when KindOf(typed.NarrowedType) == kind:
                {
                    if(OfType(typed.NarrowedType) is not { } named)
                    {
                        return null;
                    }

                    return typed.Refinement is null ? named : Of(typed.Refinement, kind) is { } part ? named.And(part) : null;
                }

            case CatchAllPattern:
                return RealSet.Everything(kind);

            default:
                return null;
        }
    }

    public static RealSet? OfValue(Expression? expression, Func<LocalVariable, RealSet?>? held = null)
    {
        if(expression is null || KindOf(expression.Type) is not { } kind)
        {
            return null;
        }

        return expression switch
        {
            LocalReferenceExpression local => held?.Invoke(local.Local)?.ConvertedTo(kind),
            CloneExpression clone => OfValue(clone.Source, held)?.ConvertedTo(kind),
            _ => LiteralOf(expression) is { } value ? RealSet.Exactly(kind, value) : null,
        };
    }

    public static LiteralValue? LiteralOf(Expression? expression, Func<LocalVariable, LiteralValue?>? held = null)
    {
        return expression switch
        {
            LiteralExpression { Value: float number, Type: var type } => new LiteralValue(number, Requirement.Unrefined(type).FullyQualifiedName),
            LiteralExpression { Value: double number, Type: var type } => new LiteralValue(number, Requirement.Unrefined(type).FullyQualifiedName),
            CloneExpression clone => LiteralOf(clone.Source, held),
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operator: null or { Modifiers.IsIntrinsic: true }, Operand: var operand }
                when LiteralOf(operand, held) is { Value: float or double } binary => Negated(binary),
            _ => LiteralValue.Of(expression, held),
        };
    }

    public static bool IsIntegralName(string name)
    {
        return name == WellKnownTypes.Int32.FullyQualifiedName || name == WellKnownTypes.Int64.FullyQualifiedName
            || name == WellKnownTypes.Int16.FullyQualifiedName || name == WellKnownTypes.UInt32.FullyQualifiedName
            || name == WellKnownTypes.UInt64.FullyQualifiedName || name == WellKnownTypes.UInt16.FullyQualifiedName
            || name == WellKnownTypes.Byte.FullyQualifiedName || name == WellKnownTypes.SByte.FullyQualifiedName
            || name == WellKnownTypes.Char.FullyQualifiedName;
    }

    private static bool IsFinite(LiteralValue value)
    {
        return value.Value switch
        {
            float number => float.IsFinite(number),
            double number => double.IsFinite(number),
            _ => true,
        };
    }

    private static LiteralValue Negated(LiteralValue value)
    {
        return value.Value is float single ? new LiteralValue(-single, value.TypeName) : new LiteralValue(-(double)value.Value, value.TypeName);
    }

    private static RealSet? Combined(IEnumerable<Pattern> operands, RealKind kind, bool conjunction)
    {
        var result = conjunction ? RealSet.Everything(kind) : RealSet.Nothing(kind);

        foreach(var operand in operands)
        {
            if(Of(operand, kind) is not { } part)
            {
                return null;
            }

            result = conjunction ? result.And(part) : result.Or(part);
        }

        return result;
    }
}