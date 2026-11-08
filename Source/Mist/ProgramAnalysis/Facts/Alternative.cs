using System.Collections.Immutable;

namespace Mist.ProgramAnalysis.Facts;

internal sealed class Alternative : IEquatable<Alternative>
{
    public static readonly Alternative Empty = new([]);

    public Alternative(IEnumerable<Fact> facts, bool exhausted = false)
    {
        Facts = [.. facts.Distinct()];
        IsExhausted = exhausted;
    }

    public ImmutableArray<Fact> Facts { get; }
    public bool IsExhausted { get; }

    public Alternative Add(IEnumerable<Fact> facts, bool exhausted = false)
    {
        return new Alternative(Facts.Concat(facts), IsExhausted || exhausted);
    }

    public bool Equals(Alternative? other)
    {
        return other is not null && IsExhausted == other.IsExhausted && Facts.Length == other.Facts.Length && Facts.ToHashSet().SetEquals(other.Facts);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Alternative);
    }

    public override int GetHashCode()
    {
        return Facts.Aggregate(IsExhausted.GetHashCode(), (hash, fact) => hash ^ fact.GetHashCode());
    }

    public override string ToString()
    {
        return string.Join(" and ", Facts);
    }
}