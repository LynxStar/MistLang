using System.Numerics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Facts;

/// <summary>
/// Facts over float, double and decimal values. The chain orders values but cannot say NaN, so a fact about a binary value is
/// recorded only where the value is known not to be NaN, and every such fact other than a copy (<c>a - b == 0</c> or
/// <c>a + b == 0</c>) therefore tells <see cref="Ordered"/> its values are not NaN. An infinity is the end of the order, which
/// is how a comparison treats it; a set's segment that reaches the type's largest value and holds the infinity beyond it is
/// unbounded on that side.
/// </summary>
internal static class RealFacts
{
    public static Fact Compare(Term value, ComparisonKind kind, Term bound)
    {
        var difference = value.Subtract(bound);
        return kind switch
        {
            ComparisonKind.Equal => new Fact(difference, Relation.Equal),
            ComparisonKind.LessThan => new Fact(difference, Relation.LessThan),
            ComparisonKind.LessThanOrEqual => new Fact(difference),
            ComparisonKind.GreaterThan => new Fact(difference.Scale(-1), Relation.LessThan),
            _ => new Fact(difference.Scale(-1)),
        };
    }

    public static Fact Compare(Term value, ComparisonKind kind, Rational bound)
    {
        return Compare(value.Scale(bound.Denominator), kind, new Term(bound.Numerator));
    }

    public static Fact? Bound(RealKind kind, Term value, ComparisonKind comparison, Term limit)
    {
        return limit is { Constant.IsZero: true, Variables: [var bound] } && limit.Multipliers[0].IsOne && RealRequirement.KindOf(bound.Type) == kind
            ? Compare(value, comparison, limit) : null;
    }

    public static IReadOnlyList<Alternative>? Within(RealSet set, Term value)
    {
        if(set.IncludesNaN || Segments(set) is not { } segments)
        {
            return null;
        }

        return [.. segments.Select(segment => new Alternative(new[]
        {
            segment.Low is { } low ? Compare(value, low.Included ? ComparisonKind.GreaterThanOrEqual : ComparisonKind.GreaterThan, low.Point) : null,
            segment.High is { } high ? Compare(value, high.Included ? ComparisonKind.LessThanOrEqual : ComparisonKind.LessThan, high.Point) : null,
        }.OfType<Fact>()))];
    }

    public static IReadOnlyList<Alternative>? Denial(RealSet set, Term value)
    {
        if(Segments(set) is not { } segments)
        {
            return null;
        }

        if(segments.Count == 0)
        {
            return [Alternative.Empty];
        }

        var denial = new List<Alternative>();
        if(segments[0].Low is { } first)
        {
            denial.Add(new Alternative([Below(set.Kind, first, value)]));
        }

        for(var i = 1; i < segments.Count; i++)
        {
            denial.Add(new Alternative([Above(set.Kind, segments[i - 1].High!.Value, value), Below(set.Kind, segments[i].Low!.Value, value)]));
        }

        if(segments[^1].High is { } last)
        {
            denial.Add(new Alternative([Above(set.Kind, last, value)]));
        }

        return denial;
    }

    public static bool Ordered(Bag bag, Value value)
    {
        if(bag.Unreachable)
        {
            return true;
        }

        foreach(var alternative in bag.Alternatives)
        {
            if(!bag.Relevant([value], alternative.Facts).Any(Orders))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Orders(Fact fact)
    {
        return fact.Values is null && !fact.Term.Variables.IsEmpty && !(fact.Relation == Relation.Equal && fact.Term.Constant.IsZero
            && fact.Term.Multipliers is [var first, var second] && BigInteger.Abs(first).IsOne && BigInteger.Abs(second).IsOne);
    }

    private static Fact Above(RealKind kind, End end, Term value)
    {
        if(kind != RealKind.Decimal && end.Binary < Largest(kind))
        {
            return Compare(value, ComparisonKind.GreaterThanOrEqual, RealNumbers.Of(kind, new RealPoint(Next(kind, end.Binary, higher: true), 0)));
        }

        return Compare(value, end.Included ? ComparisonKind.GreaterThan : ComparisonKind.GreaterThanOrEqual, end.Point);
    }

    private static Fact Below(RealKind kind, End end, Term value)
    {
        if(kind != RealKind.Decimal && end.Binary > -Largest(kind))
        {
            return Compare(value, ComparisonKind.LessThanOrEqual, RealNumbers.Of(kind, new RealPoint(Next(kind, end.Binary, higher: false), 0)));
        }

        return Compare(value, end.Included ? ComparisonKind.LessThan : ComparisonKind.LessThanOrEqual, end.Point);
    }

    private static List<(End? Low, End? High)>? Segments(RealSet set)
    {
        var kind = set.Kind;
        var segments = set.Intervals.Select(interval => (Low: (End?)Of(kind, interval.Low, interval.IncludesLow), High: (End?)Of(kind, interval.High, interval.IncludesHigh))).ToList();
        var lowest = kind == RealKind.Decimal ? set.Intervals is [var bottom, ..] && bottom.Low.Decimal == decimal.MinValue
            : set.IncludesNegativeInfinity;
        var highest = kind == RealKind.Decimal ? set.Intervals is [.., var top] && top.High.Decimal == decimal.MaxValue
            : set.IncludesPositiveInfinity;
        if(lowest)
        {
            if(segments.Count == 0 || (kind != RealKind.Decimal && segments[0].Low!.Value.Binary != -Largest(kind)))
            {
                return null;
            }

            segments[0] = (null, segments[0].High);
        }

        if(highest)
        {
            if(segments.Count == 0 || (kind != RealKind.Decimal && segments[^1].High!.Value.Binary != Largest(kind)))
            {
                return null;
            }

            segments[^1] = (segments[^1].Low, null);
        }

        return segments;
    }

    private static End Of(RealKind kind, RealPoint point, bool included)
    {
        return new End(RealNumbers.Of(kind, point), included, point.Binary);
    }

    private static double Largest(RealKind kind)
    {
        return kind == RealKind.Single ? float.MaxValue : double.MaxValue;
    }

    private static double Next(RealKind kind, double value, bool higher)
    {
        return kind == RealKind.Single ? higher ? float.BitIncrement((float)value) : float.BitDecrement((float)value)
            : higher ? double.BitIncrement(value) : double.BitDecrement(value);
    }

    private readonly record struct End(Rational Point, bool Included, double Binary);
}