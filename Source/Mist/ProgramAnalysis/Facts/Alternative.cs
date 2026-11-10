using System.Collections.Immutable;

namespace Mist.ProgramAnalysis.Facts;

internal sealed class Alternative : IEquatable<Alternative>
{
    public static readonly Alternative Empty = new([]);

    private readonly HashSet<Fact> contents;
    private readonly int hash;
    private readonly bool contradicted;
    private FactIndex? index;

    public Alternative(IEnumerable<Fact> facts, bool exhausted = false)
    {
        contents = [];
        var ordered = ImmutableArray.CreateBuilder<Fact>();
        foreach(var fact in facts)
        {
            if(contents.Add(fact))
            {
                ordered.Add(fact);
            }
        }

        Facts = ordered.ToImmutable();
        IsExhausted = exhausted;
        hash = Facts.Aggregate(IsExhausted.GetHashCode(), (hash, fact) => hash ^ fact.GetHashCode());
        contradicted = Facts.Any(fact => fact.IsContradiction);
    }

    private Alternative(Alternative previous, IReadOnlyList<Fact> added, bool exhausted)
    {
        contents = added.Count == 0 ? previous.contents : new HashSet<Fact>(previous.contents);
        foreach(var fact in added)
        {
            _ = contents.Add(fact);
        }

        Facts = previous.Facts.AddRange(added);
        IsExhausted = previous.IsExhausted || exhausted;
        hash = added.Aggregate(previous.hash ^ previous.IsExhausted.GetHashCode() ^ IsExhausted.GetHashCode(), (hash, fact) => hash ^ fact.GetHashCode());
        contradicted = previous.contradicted || added.Any(fact => fact.IsContradiction);
        index = Volatile.Read(ref previous.index);
        for(var i = 0; index is not null && i < added.Count; i++)
        {
            index = index.With(added[i], previous.Facts.Length + i);
        }
    }

    public ImmutableArray<Fact> Facts { get; }
    public bool IsExhausted { get; }

    public Alternative Add(IEnumerable<Fact> facts, bool exhausted = false)
    {
        var added = facts.Where(fact => !contents.Contains(fact)).Distinct().ToList();
        return added.Count == 0 && (IsExhausted || !exhausted)
            ? this : new Alternative(this, added, exhausted);
    }

    public bool Equals(Alternative? other)
    {
        return other is not null && IsExhausted == other.IsExhausted && Facts.Length == other.Facts.Length && hash == other.hash && contents.SetEquals(other.contents);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Alternative);
    }

    public override int GetHashCode()
    {
        return hash;
    }

    internal IntegralSet Bound(Value value)
    {
        return contradicted ? IntegralSet.Nothing : Index.Bound(value);
    }

    internal ImmutableArray<Fact> Relevant(IEnumerable<Value> from)
    {
        var indexed = Index;
        var pending = new Stack<Value>(from);
        var reached = new HashSet<Value>();
        var positions = new HashSet<int>();
        while(pending.TryPop(out var value))
        {
            if(!reached.Add(value))
            {
                continue;
            }

            foreach(var position in indexed.Of(value))
            {
                if(positions.Add(position))
                {
                    foreach(var next in Facts[position].Term.Variables)
                    {
                        pending.Push(next);
                    }
                }
            }
        }

        positions.UnionWith(indexed.Constants);
        var ordered = positions.ToArray();
        Array.Sort(ordered);
        return [.. ordered.Select(position => Facts[position])];
    }

    private FactIndex Index
    {
        get
        {
            if(Volatile.Read(ref index) is { } known)
            {
                return known;
            }

            var created = FactIndex.Of(Facts);
            return Interlocked.CompareExchange(ref index, created, null) ?? created;
        }
    }

    public override string ToString()
    {
        return string.Join(" and ", Facts);
    }
}