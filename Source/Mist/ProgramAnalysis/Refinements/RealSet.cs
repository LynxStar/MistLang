using System.Collections.Immutable;
using System.Globalization;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Refinements;

// Binary cutoffs name representable values; decimal cutoffs retain openness. Nonfinite values never stand for finite endpoints.
internal sealed class RealSet : IEquatable<RealSet>
{
    private RealSet(RealKind kind, ImmutableArray<RealInterval> intervals, bool nan, bool positiveInfinity, bool negativeInfinity)
    {
        Kind = kind;
        Intervals = intervals;
        IncludesNaN = nan;
        IncludesPositiveInfinity = positiveInfinity;
        IncludesNegativeInfinity = negativeInfinity;
    }

    public RealKind Kind { get; }

    public ImmutableArray<RealInterval> Intervals { get; }

    public bool IncludesNaN { get; }

    public bool IncludesPositiveInfinity { get; }

    public bool IncludesNegativeInfinity { get; }

    public static RealSet Everything(RealKind kind)
    {
        return new RealSet(kind, [Universe(kind)], kind != RealKind.Decimal, kind != RealKind.Decimal, kind != RealKind.Decimal);
    }

    public static RealSet Nothing(RealKind kind)
    {
        return new RealSet(kind, [], false, false, false);
    }

    public static RealSet? Exactly(RealKind kind, LiteralValue value)
    {
        return Value(kind, value, false) is { } number ? At(kind, number) : null;
    }

    public static RealSet? Compare(RealKind kind, ComparisonKind comparison, LiteralValue bound)
    {
        if(Value(kind, bound, true) is not { } number)
        {
            return null;
        }

        if(number.IsNaN)
        {
            return Nothing(kind);
        }

        var universe = Universe(kind);
        var intervals = number.Infinity != 0
            ? (Holds(comparison, -number.Infinity) ? new[] { universe } : [])
            : comparison switch
            {
                ComparisonKind.Equal => [new RealInterval(number.Point, true, number.Point, true)],
                ComparisonKind.LessThan => [universe with { High = number.Point, IncludesHigh = false }],
                ComparisonKind.LessThanOrEqual => [universe with { High = number.Point }],
                ComparisonKind.GreaterThan => [universe with { Low = number.Point, IncludesLow = false }],
                ComparisonKind.GreaterThanOrEqual => [universe with { Low = number.Point }],
                _ => [],
            };
        var positive = kind != RealKind.Decimal && Holds(comparison, number.Infinity == 1 ? 0 : 1);
        var negative = kind != RealKind.Decimal && Holds(comparison, number.Infinity == -1 ? 0 : -1);
        return Normalised(kind, intervals, false, positive, negative);
    }

    public bool Contains(LiteralValue value)
    {
        if(Value(Kind, value, false) is not { } number)
        {
            return false;
        }

        return number.IsNaN ? IncludesNaN : number.Infinity switch
        {
            1 => IncludesPositiveInfinity,
            -1 => IncludesNegativeInfinity,
            _ => Intervals.Any(interval => Inside(number.Point, interval)),
        };
    }

    public bool IsSubsetOf(RealSet other)
    {
        return Kind == other.Kind && (!IncludesNaN || other.IncludesNaN)
            && (!IncludesPositiveInfinity || other.IncludesPositiveInfinity)
            && (!IncludesNegativeInfinity || other.IncludesNegativeInfinity)
            && Intervals.All(mine => other.Intervals.Any(theirs => Covers(theirs, mine)));
    }

    public override string ToString()
    {
        var parts = Intervals.Select(interval =>
            $"{(interval.IncludesLow ? "[" : "(")}{Display(interval.Low)}..{Display(interval.High)}{(interval.IncludesHigh ? "]" : ")")}").ToList();
        if(IncludesNaN)
        {
            parts.Add("NaN");
        }

        if(IncludesPositiveInfinity)
        {
            parts.Add("+Infinity");
        }

        if(IncludesNegativeInfinity)
        {
            parts.Add("-Infinity");
        }

        return parts.Count == 0 ? "nothing" : string.Join(" or ", parts);
    }

    private string Display(RealPoint point)
    {
        return Kind == RealKind.Decimal ? point.Decimal.ToString(CultureInfo.InvariantCulture)
            : Kind == RealKind.Single ? ((float)point.Binary).ToString("R", CultureInfo.InvariantCulture)
            : point.Binary.ToString("R", CultureInfo.InvariantCulture);
    }

    public RealSet And(RealSet other)
    {
        RequireSameKind(other);
        var intervals = new List<RealInterval>();

        foreach(var mine in Intervals)
        {
            foreach(var theirs in other.Intervals)
            {
                var lowOrder = Order(mine.Low, theirs.Low);
                var highOrder = Order(mine.High, theirs.High);
                intervals.Add(new RealInterval(
                    lowOrder >= 0 ? mine.Low : theirs.Low,
                    lowOrder == 0 ? mine.IncludesLow && theirs.IncludesLow : lowOrder > 0 ? mine.IncludesLow : theirs.IncludesLow,
                    highOrder <= 0 ? mine.High : theirs.High,
                    highOrder == 0 ? mine.IncludesHigh && theirs.IncludesHigh : highOrder < 0 ? mine.IncludesHigh : theirs.IncludesHigh));
            }
        }

        return Normalised(Kind, intervals, IncludesNaN && other.IncludesNaN,
            IncludesPositiveInfinity && other.IncludesPositiveInfinity, IncludesNegativeInfinity && other.IncludesNegativeInfinity);
    }

    public RealSet Or(RealSet other)
    {
        RequireSameKind(other);
        return Normalised(Kind, [.. Intervals, .. other.Intervals], IncludesNaN || other.IncludesNaN,
            IncludesPositiveInfinity || other.IncludesPositiveInfinity, IncludesNegativeInfinity || other.IncludesNegativeInfinity);
    }

    public RealSet Not()
    {
        var universe = Universe(Kind);
        var intervals = new List<RealInterval>();
        var next = universe.Low;
        var included = true;

        foreach(var interval in Intervals)
        {
            intervals.Add(new RealInterval(next, included, interval.Low, !interval.IncludesLow));
            next = interval.High;
            included = !interval.IncludesHigh;
        }

        intervals.Add(new RealInterval(next, included, universe.High, true));
        return Normalised(Kind, intervals, Kind != RealKind.Decimal && !IncludesNaN,
            Kind != RealKind.Decimal && !IncludesPositiveInfinity, Kind != RealKind.Decimal && !IncludesNegativeInfinity);
    }

    public RealSet? ConvertedTo(RealKind kind)
    {
        if(kind == Kind)
        {
            return this;
        }

        return Kind == RealKind.Single && kind == RealKind.Double
            ? Normalised(kind, Intervals, IncludesNaN, IncludesPositiveInfinity, IncludesNegativeInfinity)
            : null;
    }

    public RealSet WidenedAgainst(RealSet previous)
    {
        var joined = Or(previous);

        if(joined.Intervals.Length == 0)
        {
            return joined;
        }

        var universe = Universe(Kind);
        var low = joined.Intervals[0].Low;
        var includesLow = joined.Intervals[0].IncludesLow;
        var high = joined.Intervals[^1].High;
        var includesHigh = joined.Intervals[^1].IncludesHigh;

        if(previous.Intervals.Length > 0)
        {
            if(Order(low, previous.Intervals[0].Low) < 0 || (Order(low, previous.Intervals[0].Low) == 0 && includesLow && !previous.Intervals[0].IncludesLow))
            {
                low = universe.Low;
                includesLow = true;
            }

            if(Order(high, previous.Intervals[^1].High) > 0 || (Order(high, previous.Intervals[^1].High) == 0 && includesHigh && !previous.Intervals[^1].IncludesHigh))
            {
                high = universe.High;
                includesHigh = true;
            }
        }

        return Normalised(Kind, [new RealInterval(low, includesLow, high, includesHigh)], joined.IncludesNaN,
            joined.IncludesPositiveInfinity, joined.IncludesNegativeInfinity);
    }

    public bool Equals(RealSet? other)
    {
        return other is not null && Kind == other.Kind && IncludesNaN == other.IncludesNaN
            && IncludesPositiveInfinity == other.IncludesPositiveInfinity && IncludesNegativeInfinity == other.IncludesNegativeInfinity
            && Intervals.SequenceEqual(other.Intervals);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as RealSet);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(IncludesNaN);
        hash.Add(IncludesPositiveInfinity);
        hash.Add(IncludesNegativeInfinity);

        foreach(var interval in Intervals)
        {
            hash.Add(interval);
        }

        return hash.ToHashCode();
    }

    private static RealSet At(RealKind kind, RealNumber number)
    {
        return number.IsNaN ? new RealSet(kind, [], true, false, false) : number.Infinity switch
        {
            1 => new RealSet(kind, [], false, true, false),
            -1 => new RealSet(kind, [], false, false, true),
            _ => Normalised(kind, [new RealInterval(number.Point, true, number.Point, true)], false, false, false),
        };
    }

    private static RealNumber? Value(RealKind kind, LiteralValue value, bool comparison)
    {
        if(RealRequirement.KindOfName(value.TypeName) is { } source)
        {
            if(source == RealKind.Decimal)
            {
                return kind == RealKind.Decimal && value.Value is decimal number ? new RealNumber(new RealPoint(0, number)) : null;
            }

            if(kind == RealKind.Decimal || (kind == RealKind.Single && source == RealKind.Double && !comparison))
            {
                return null;
            }

            return value.Value switch
            {
                float number when source == RealKind.Single => Binary(number),
                double number when source == RealKind.Double => Binary(number),
                _ => null,
            };
        }

        if(!RealRequirement.IsIntegralName(value.TypeName) || value.Value is not sbyte and not byte and not short and not ushort and not int and not uint and not long and not ulong and not char)
        {
            return null;
        }

        var integral = value.Value is char character ? (int)character : value.Value;
        return kind switch
        {
            RealKind.Single => Binary(Convert.ToSingle(integral, CultureInfo.InvariantCulture)),
            RealKind.Double => Binary(Convert.ToDouble(integral, CultureInfo.InvariantCulture)),
            RealKind.Decimal => new RealNumber(new RealPoint(0, Convert.ToDecimal(integral, CultureInfo.InvariantCulture))),
            _ => null,
        };
    }

    private static RealNumber Binary(double number)
    {
        return new RealNumber(new RealPoint(number == 0 ? 0 : number, 0), double.IsNaN(number), double.IsPositiveInfinity(number) ? 1 : double.IsNegativeInfinity(number) ? -1 : 0);
    }

    private static bool Holds(ComparisonKind comparison, int order)
    {
        return comparison switch
        {
            ComparisonKind.Equal => order == 0,
            ComparisonKind.LessThan => order < 0,
            ComparisonKind.LessThanOrEqual => order <= 0,
            ComparisonKind.GreaterThan => order > 0,
            ComparisonKind.GreaterThanOrEqual => order >= 0,
            _ => false,
        };
    }

    private bool Inside(RealPoint point, RealInterval interval)
    {
        var low = Order(point, interval.Low);
        var high = Order(point, interval.High);
        return (low > 0 || (low == 0 && interval.IncludesLow)) && (high < 0 || (high == 0 && interval.IncludesHigh));
    }

    private bool Covers(RealInterval outer, RealInterval inner)
    {
        var low = Order(outer.Low, inner.Low);
        var high = Order(outer.High, inner.High);
        return (low < 0 || (low == 0 && (outer.IncludesLow || !inner.IncludesLow)))
            && (high > 0 || (high == 0 && (outer.IncludesHigh || !inner.IncludesHigh)));
    }

    private int Order(RealPoint left, RealPoint right)
    {
        return ComparePoints(Kind, left, right);
    }

    private void RequireSameKind(RealSet other)
    {
        if(Kind != other.Kind)
        {
            throw new ArgumentException("Real set operations require the same runtime domain.", nameof(other));
        }
    }

    private static RealInterval Universe(RealKind kind)
    {
        return kind switch
        {
            RealKind.Single => new RealInterval(new RealPoint(-float.MaxValue, 0), true, new RealPoint(float.MaxValue, 0), true),
            RealKind.Double => new RealInterval(new RealPoint(-double.MaxValue, 0), true, new RealPoint(double.MaxValue, 0), true),
            _ => new RealInterval(new RealPoint(0, decimal.MinValue), true, new RealPoint(0, decimal.MaxValue), true),
        };
    }

    private static RealSet Normalised(RealKind kind, IEnumerable<RealInterval> intervals, bool nan, bool positiveInfinity, bool negativeInfinity)
    {
        var sorted = intervals.Select(interval => Finite(kind, interval)).OfType<RealInterval>()
            .OrderBy(interval => kind == RealKind.Decimal ? interval.Low.Decimal : 0)
            .ThenBy(interval => kind == RealKind.Decimal ? 0 : interval.Low.Binary)
            .ThenByDescending(interval => interval.IncludesLow);
        var merged = new List<RealInterval>();

        foreach(var interval in sorted)
        {
            var order = merged.Count == 0 ? -1 : ComparePoints(kind, interval.Low, merged[^1].High);
            var adjacent = kind != RealKind.Decimal && merged.Count > 0 && interval.Low.Binary == Next(kind, merged[^1].High.Binary, true);

            if(merged.Count > 0 && (order < 0 || (order == 0 && (interval.IncludesLow || merged[^1].IncludesHigh)) || adjacent))
            {
                var last = merged[^1];
                var highOrder = ComparePoints(kind, last.High, interval.High);
                merged[^1] = highOrder < 0 ? last with { High = interval.High, IncludesHigh = interval.IncludesHigh }
                    : highOrder == 0 ? last with { IncludesHigh = last.IncludesHigh || interval.IncludesHigh } : last;
            }
            else
            {
                merged.Add(interval);
            }
        }

        return new RealSet(kind, [.. merged], nan, positiveInfinity, negativeInfinity);
    }

    private static RealInterval? Finite(RealKind kind, RealInterval interval)
    {
        var universe = Universe(kind);

        if(ComparePoints(kind, interval.Low, universe.Low) < 0)
        {
            interval = interval with { Low = universe.Low, IncludesLow = true };
        }

        if(ComparePoints(kind, interval.High, universe.High) > 0)
        {
            interval = interval with { High = universe.High, IncludesHigh = true };
        }

        if(kind != RealKind.Decimal)
        {
            var low = Round(kind, interval.Low.Binary, true, interval.IncludesLow);
            var high = Round(kind, interval.High.Binary, false, interval.IncludesHigh);
            interval = new RealInterval(new RealPoint(low == 0 ? 0 : low, 0), true, new RealPoint(high == 0 ? 0 : high, 0), true);
        }

        var order = ComparePoints(kind, interval.Low, interval.High);
        return order < 0 || (order == 0 && interval.IncludesLow && interval.IncludesHigh) ? interval : null;
    }

    private static double Round(RealKind kind, double bound, bool lower, bool included)
    {
        var rounded = kind == RealKind.Single ? (float)bound : bound;
        return lower ? rounded < bound || (rounded == bound && !included) ? Next(kind, rounded, true) : rounded
            : rounded > bound || (rounded == bound && !included) ? Next(kind, rounded, false) : rounded;
    }

    private static double Next(RealKind kind, double value, bool higher)
    {
        return kind == RealKind.Single ? higher ? float.BitIncrement((float)value) : float.BitDecrement((float)value)
            : higher ? double.BitIncrement(value) : double.BitDecrement(value);
    }

    private static int ComparePoints(RealKind kind, RealPoint left, RealPoint right)
    {
        return kind == RealKind.Decimal ? left.Decimal.CompareTo(right.Decimal) : left.Binary.CompareTo(right.Binary);
    }

    private readonly record struct RealNumber(RealPoint Point, bool IsNaN = false, int Infinity = 0);
}

internal enum RealKind
{
    Single,
    Double,
    Decimal,
}

internal readonly record struct RealPoint(double Binary, decimal Decimal);

internal readonly record struct RealInterval(RealPoint Low, bool IncludesLow, RealPoint High, bool IncludesHigh);