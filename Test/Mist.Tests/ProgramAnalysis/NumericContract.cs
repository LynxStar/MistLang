using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

internal sealed class NumericContract(string domain)
{
    private readonly HandBuiltRefinements values = new();

    public IMistType TypeFor(string shape, string? name = null)
    {
        var type = values.Built.Type(domain);
        var zero = Number(domain, 0);
        var one = Number(domain, 1);
        Pattern compared(ComparisonKind op, object value)
        {
            return values.Compare(type, op, value);
        }

        Pattern between(ComparisonKind left, object low, ComparisonKind right, object high)
        {
            return new ConjunctionPattern(HandBuiltModel.Node(), type, [compared(left, low), compared(right, high)]);
        }

        Pattern negate(ComparisonKind op)
        {
            return new NegatedPattern(HandBuiltModel.Node(), type, compared(op, zero));
        }

        if(shape.Length == 0)
        {
            return type;
        }

        var predicate = shape switch
        {
            "positive" => compared(ComparisonKind.GreaterThan, zero),
            "nonnegative" => compared(ComparisonKind.GreaterThanOrEqual, zero),
            "nonpositive" => compared(ComparisonKind.LessThanOrEqual, zero),
            "nonzero" => negate(ComparisonKind.Equal),
            "complement of positive" => negate(ComparisonKind.GreaterThan),
            "complement of nonpositive" => negate(ComparisonKind.LessThanOrEqual),
            "zero through one hundred" => between(ComparisonKind.GreaterThanOrEqual, zero, ComparisonKind.LessThanOrEqual, 100L),
            "zero through ten" => between(ComparisonKind.GreaterThanOrEqual, zero, ComparisonKind.LessThanOrEqual, 10L),
            "zero through nine" => between(ComparisonKind.GreaterThanOrEqual, zero, ComparisonKind.LessThanOrEqual, 9L),
            "one through nine" => between(ComparisonKind.GreaterThanOrEqual, one, ComparisonKind.LessThanOrEqual, 9L),
            "open unit" => between(ComparisonKind.GreaterThan, zero, ComparisonKind.LessThan, one),
            "half-open unit" => between(ComparisonKind.GreaterThanOrEqual, zero, ComparisonKind.LessThan, one),
            "closed unit" => between(ComparisonKind.GreaterThanOrEqual, zero, ComparisonKind.LessThanOrEqual, one),
            "at most float tenth" => compared(ComparisonKind.LessThanOrEqual, 0.1f),
            "at most double tenth" => compared(ComparisonKind.LessThanOrEqual, 0.1),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        return HandBuiltRefinements.RefinePattern(type, predicate, name);
    }

    public static object Number(string domain, int value)
    {
        return domain switch
        {
            "Int32" => (long)value,
            "Single" => (float)value,
            "Double" => (double)value,
            "Decimal" => (decimal)value,
            _ => throw new ArgumentOutOfRangeException(nameof(domain)),
        };
    }
}