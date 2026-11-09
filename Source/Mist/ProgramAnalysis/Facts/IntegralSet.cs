using System.Collections.Immutable;
using System.Numerics;
using Mist.ProgramAnalysis.Refinements;

namespace Mist.ProgramAnalysis.Facts;

internal readonly record struct IntegralInterval(BigInteger? Low, BigInteger? High);

internal sealed class IntegralSet : IEquatable<IntegralSet>
{
    public static readonly IntegralSet Everything = new([new(null, null)]);
    public static readonly IntegralSet Nothing = new([]);

    public IntegralSet(IEnumerable<IntegralInterval> intervals)
    {
        var sorted = intervals.Where(interval => interval.Low is null || interval.High is null || interval.Low <= interval.High).ToList();
        sorted.Sort((left, right) => left.Low is { } low ? right.Low is { } otherLow ? low.CompareTo(otherLow) : 1 : right.Low is null ? 0 : -1);
        var result = new List<IntegralInterval>();
        foreach(var interval in sorted)
        {
            if(result.Count > 0 && (result[^1].High is null || interval.Low is null || interval.Low <= result[^1].High + 1))
            {
                var high = result[^1].High is { } previous && interval.High is { } next ? (BigInteger?)BigInteger.Max(previous, next) : null;
                result[^1] = new IntegralInterval(result[^1].Low, high);
            }
            else
            {
                result.Add(interval);
            }
        }

        Intervals = [.. result];
        var hash = new HashCode();
        foreach(var interval in Intervals)
        {
            hash.Add(interval);
        }

        hashCode = hash.ToHashCode();
    }

    private readonly int hashCode;

    public ImmutableArray<IntegralInterval> Intervals { get; }
    public bool IsEmpty => Intervals.IsEmpty;
    private bool IsEverything => Intervals is [{ Low: null, High: null }];

    public static IntegralSet Of(Admitted admitted)
    {
        return new IntegralSet(admitted.Intervals.Select(interval => new IntegralInterval(interval.Low, interval.High)));
    }

    public IntegralSet Or(IntegralSet other)
    {
        return other.IsEmpty ? this : IsEmpty ? other : new IntegralSet(Intervals.Concat(other.Intervals));
    }

    public IntegralSet And(IntegralSet other)
    {
        if(IsEverything || other.IsEmpty)
        {
            return other;
        }

        if(other.IsEverything || IsEmpty)
        {
            return this;
        }

        return new IntegralSet(from left in Intervals
                               from right in other.Intervals
                               select new IntegralInterval(
                                   left.Low is { } low && right.Low is { } otherLow ? BigInteger.Max(low, otherLow) : left.Low ?? right.Low,
                                   left.High is { } high && right.High is { } otherHigh ? BigInteger.Min(high, otherHigh) : left.High ?? right.High));
    }

    public bool Contains(BigInteger value)
    {
        return Intervals.Any(interval => (interval.Low is null || interval.Low <= value) && (interval.High is null || interval.High >= value));
    }

    public bool Equals(IntegralSet? other)
    {
        return ReferenceEquals(this, other) || (other is not null && hashCode == other.hashCode && Intervals.SequenceEqual(other.Intervals));
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as IntegralSet);
    }

    public override int GetHashCode()
    {
        return hashCode;
    }
}