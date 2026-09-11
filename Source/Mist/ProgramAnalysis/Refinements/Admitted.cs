using System.Collections.Immutable;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// The values a predicate over one integral quantity admits, as a sorted list of disjoint closed
/// intervals, so that every question the prover asks -- does a constant satisfy it, does one predicate
/// imply another, what do two facts leave when both hold -- is a set operation with an exact answer.
/// A code point and a truth are integers too; a real is not represented, and a predicate over one is
/// simply not proven.
/// </summary>
internal sealed class Admitted : IEquatable<Admitted>
{
    public static readonly Admitted Everything = new([new Interval(long.MinValue, long.MaxValue)]);
    public static readonly Admitted Nothing = new([]);

    private Admitted(ImmutableArray<Interval> intervals)
    {
        Intervals = intervals;
    }

    public ImmutableArray<Interval> Intervals { get; }

    public bool IsEverything => Intervals.Length == 1 && Intervals[0].Low == long.MinValue && Intervals[0].High == long.MaxValue;

    public long? Low => Intervals.Length == 0 ? null : Intervals[0].Low;

    public long? High => Intervals.Length == 0 ? null : Intervals[^1].High;

    public static Admitted Exactly(long value)
    {
        return new Admitted([new Interval(value, value)]);
    }

    public static Admitted AtLeast(long value)
    {
        return new Admitted([new Interval(value, long.MaxValue)]);
    }

    public static Admitted AtMost(long value)
    {
        return new Admitted([new Interval(long.MinValue, value)]);
    }

    public static Admitted Above(long value)
    {
        return value == long.MaxValue ? Nothing : AtLeast(value + 1);
    }

    public static Admitted Below(long value)
    {
        return value == long.MinValue ? Nothing : AtMost(value - 1);
    }

    public static Admitted Between(long low, long high)
    {
        return low > high ? Nothing : new Admitted([new Interval(low, high)]);
    }

    public bool Contains(long value)
    {
        return Intervals.Any(interval => interval.Low <= value && value <= interval.High);
    }

    public bool IsSubsetOf(Admitted other)
    {
        return Intervals.All(mine => other.Intervals.Any(theirs => theirs.Low <= mine.Low && mine.High <= theirs.High));
    }

    public Admitted And(Admitted other)
    {
        var result = new List<Interval>();

        foreach(var mine in Intervals)
        {
            foreach(var theirs in other.Intervals)
            {
                var low = Math.Max(mine.Low, theirs.Low);
                var high = Math.Min(mine.High, theirs.High);

                if(low <= high)
                {
                    result.Add(new Interval(low, high));
                }
            }
        }

        return Normalised(result);
    }

    public Admitted Or(Admitted other)
    {
        return Normalised([.. Intervals, .. other.Intervals]);
    }

    public Admitted Not()
    {
        var result = new List<Interval>();
        var next = long.MinValue;
        var open = true;

        foreach(var interval in Intervals)
        {
            if(open && interval.Low > next)
            {
                result.Add(new Interval(next, interval.Low - 1));
            }

            open = interval.High < long.MaxValue;
            next = open ? interval.High + 1 : long.MaxValue;
        }

        if(open)
        {
            result.Add(new Interval(next, long.MaxValue));
        }

        return Normalised(result);
    }

    public Admitted Hull()
    {
        return Intervals.Length <= 1 ? this : new Admitted([new Interval(Intervals[0].Low, Intervals[^1].High)]);
    }

    public Admitted Shifted(long by)
    {
        return Normalised(Intervals.Select(interval => new Interval(Saturating(interval.Low, by), Saturating(interval.High, by))).ToList());
    }

    public Admitted WidenedAgainst(Admitted previous)
    {
        if(Intervals.Length == 0 || previous.Intervals.Length == 0)
        {
            return this;
        }

        var low = Intervals[0].Low < previous.Intervals[0].Low ? long.MinValue : Intervals[0].Low;
        var high = Intervals[^1].High > previous.Intervals[^1].High ? long.MaxValue : Intervals[^1].High;
        return Between(low, high);
    }

    public bool Equals(Admitted? other)
    {
        return other is not null && Intervals.SequenceEqual(other.Intervals);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Admitted);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach(var interval in Intervals)
        {
            hash.Add(interval);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return Intervals.Length == 0 ? "nothing" : string.Join(" or ", Intervals.Select(Describe));
    }

    private static string Describe(Interval interval)
    {
        return (interval.Low, interval.High) switch
        {
            (long.MinValue, long.MaxValue) => "anything",
            (long.MinValue, var high) => $"<= {high}",
            (var low, long.MaxValue) => $">= {low}",
            (var low, var high) when low == high => $"== {low}",
            (var low, var high) => $">= {low} and <= {high}",
        };
    }

    private static long Saturating(long value, long by)
    {
        if(value is long.MinValue or long.MaxValue)
        {
            return value;
        }

        var sum = value + by;
        return by > 0 && sum < value ? long.MaxValue : by < 0 && sum > value ? long.MinValue : sum;
    }

    private static Admitted Normalised(IReadOnlyList<Interval> intervals)
    {
        var sorted = intervals.Where(interval => interval.Low <= interval.High).OrderBy(interval => interval.Low).ToList();
        var merged = new List<Interval>();

        foreach(var interval in sorted)
        {
            if(merged.Count > 0 && (merged[^1].High == long.MaxValue || interval.Low <= merged[^1].High + 1))
            {
                merged[^1] = new Interval(merged[^1].Low, Math.Max(merged[^1].High, interval.High));
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged.Count == 0 ? Nothing : new Admitted([.. merged]);
    }
}

internal readonly record struct Interval(long Low, long High);