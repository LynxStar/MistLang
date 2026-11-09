using System.Collections.Immutable;

namespace Mist.ProgramAnalysis.Facts;

internal sealed class Bag
{
    public const int AlternativeCap = 16;
    public static readonly Bag Empty = new([], [Alternative.Empty]);

    public Bag(IEnumerable<Fact> common, IEnumerable<Alternative> alternatives, Budget? mergeBudget = null)
    {
        Common = [.. common.Distinct()];
        index = FactIndex.Of(Common);
        var contradicted = Common.Any(fact => fact.IsContradiction) || index.Bounds.Values.Any(values => values.IsEmpty);
        var viable = contradicted ? [] : alternatives.Where(alternative => !Contradicted(alternative)).ToList();
        if(viable.Count > AlternativeCap)
        {
            var count = viable.Count - AlternativeCap + 1;
            var merged = MergePerVariable(viable.Take(count).Select(alternative => alternative.Add(Common)), mergeBudget);
            viable = [merged, .. viable.Skip(count)];
        }

        Unreachable = viable.Count == 0;
        Alternatives = Unreachable ? [Alternative.Empty] : [.. viable];
    }

    private Bag(ImmutableArray<Fact> common, ImmutableArray<Alternative> alternatives, FactIndex index, ImmutableDictionary<Value, IntegralSet> projections)
    {
        Common = common;
        Unreachable = alternatives.IsEmpty;
        Alternatives = Unreachable ? [Alternative.Empty] : alternatives;
        this.index = index;
        this.projections = Unreachable ? ImmutableDictionary<Value, IntegralSet>.Empty : projections;
    }

    private readonly FactIndex index;
    private ImmutableDictionary<Value, IntegralSet> projections = ImmutableDictionary<Value, IntegralSet>.Empty;

    public ImmutableArray<Fact> Common { get; }
    public ImmutableArray<Alternative> Alternatives { get; }
    public bool Unreachable { get; }

    public Bag Add(Fact fact)
    {
        if(Holds(fact))
        {
            return this;
        }

        var common = Common.Add(fact);
        var indexed = index.With(fact, Common.Length);
        var viable = Unreachable || fact.IsContradiction ? [] : Alternatives.Where(alternative =>
            !fact.Term.Variables.Any(value => indexed.Bound(value).And(Chain.SingleValue(alternative.Facts, value)).IsEmpty)).ToImmutableArray();
        var kept = viable.Length != Alternatives.Length || projections.IsEmpty ? ImmutableDictionary<Value, IntegralSet>.Empty
            : projections.RemoveRange(Connected(fact, common, indexed, viable));
        return new Bag(common, viable, indexed, kept);
    }

    public IntegralSet Intervals(Value value)
    {
        if(projections.TryGetValue(value, out var known))
        {
            return known;
        }

        var projected = Project(this, value, Budget.Default);
        _ = ImmutableInterlocked.TryAdd(ref projections, value, projected);
        return projected;
    }

    internal static IntegralSet Project(Bag bag, Value value, Budget budget)
    {
        if(bag.Unreachable)
        {
            return IntegralSet.Nothing;
        }

        var values = IntegralSet.Nothing;
        var meter = new ProofBudget(budget);
        var linked = bag.index.Of(value).Any(position => bag.Common[position].Term.Variables.Length > 1);
        foreach(var alternative in bag.Alternatives)
        {
            if(!linked && !alternative.Facts.Any(fact => fact.Term.Variables.Length > 1 && fact.Term.Variables.Contains(value)))
            {
                values = values.Or(bag.index.Bound(value).And(Chain.SingleValue(alternative.Facts, value)));
                continue;
            }

            if(Chain.ProjectIntervals(bag.Relevant([value], alternative.Facts), value, meter) is not { } projected)
            {
                return IntegralSet.Everything;
            }

            values = values.Or(projected);
        }

        return values;
    }

    /// <summary>
    /// The facts of <see cref="Common"/> followed by <paramref name="extra"/> that <see cref="Chain.Project"/> would keep for
    /// <paramref name="from"/>, in the same order, found through the index so the cost follows what is reachable and not the
    /// size of the bag.
    /// </summary>
    internal List<Fact> Relevant(IEnumerable<Value> from, IEnumerable<Fact> extra)
    {
        var others = extra.ToList();
        var reachable = new HashSet<Value>();
        var pending = new Stack<Value>(from);
        var positions = new HashSet<int>();
        var linked = new bool[others.Count];
        while(pending.Count > 0)
        {
            while(pending.TryPop(out var value))
            {
                if(!reachable.Add(value))
                {
                    continue;
                }

                foreach(var position in index.Of(value))
                {
                    if(positions.Add(position))
                    {
                        foreach(var next in Common[position].Term.Variables)
                        {
                            pending.Push(next);
                        }
                    }
                }
            }

            for(var i = 0; i < others.Count; i++)
            {
                if(!linked[i] && others[i].Term.Variables.Any(reachable.Contains))
                {
                    linked[i] = true;
                    foreach(var next in others[i].Term.Variables)
                    {
                        pending.Push(next);
                    }
                }
            }
        }

        positions.UnionWith(index.Constants);
        var ordered = positions.ToArray();
        Array.Sort(ordered);
        return [.. ordered.Select(position => Common[position]), .. others.Where((fact, i) => linked[i] || fact.Term.Variables.IsEmpty)];
    }

    private bool Holds(Fact fact)
    {
        return (fact.Term.Variables.IsEmpty ? index.Constants : index.Of(fact.Term.Variables[0])).Any(position => Common[position].Equals(fact));
    }

    private static HashSet<Value> Connected(Fact fact, ImmutableArray<Fact> common, FactIndex index, ImmutableArray<Alternative> alternatives)
    {
        var connected = new HashSet<Value>();
        var pending = new Stack<Value>(fact.Term.Variables);
        var links = alternatives.SelectMany(alternative => alternative.Facts).Where(other => other.Term.Variables.Length > 1).ToList();
        while(pending.Count > 0)
        {
            while(pending.TryPop(out var value))
            {
                if(!connected.Add(value))
                {
                    continue;
                }

                foreach(var position in index.Of(value))
                {
                    if(common[position].Term.Variables.Length > 1)
                    {
                        foreach(var next in common[position].Term.Variables)
                        {
                            pending.Push(next);
                        }
                    }
                }
            }

            foreach(var link in links.Where(link => link.Term.Variables.Any(connected.Contains)))
            {
                foreach(var next in link.Term.Variables.Where(next => !connected.Contains(next)))
                {
                    pending.Push(next);
                }
            }
        }

        return connected;
    }

    public Bag AddAlternatives(IEnumerable<Alternative> alternatives)
    {
        var added = alternatives.ToList();
        return new Bag(Common, Unreachable ? [] : Alternatives.SelectMany(existing => added.Select(next => existing.Add(next.Facts, next.IsExhausted))));
    }

    public Bag Join(Bag other)
    {
        if(Unreachable || other.Unreachable)
        {
            return Unreachable ? other : this;
        }

        var common = Common.Intersect(other.Common).ToList();
        return new Bag(common, Alternatives.Select(alternative => alternative.Add(Common.Except(common)))
            .Concat(other.Alternatives.Select(alternative => alternative.Add(other.Common.Except(common)))));
    }

    public static Alternative MergePerVariable(IEnumerable<Alternative> source, Budget? budget = null)
    {
        var alternatives = source.ToList();
        if(alternatives.Any(alternative => alternative.IsExhausted))
        {
            return new Alternative([], exhausted: true);
        }

        var meter = new ProofBudget(budget ?? Budget.Default);
        var facts = new List<Fact>();
        foreach(var value in alternatives.SelectMany(alternative => alternative.Facts).SelectMany(fact => fact.Term.Variables).Distinct())
        {
            var values = IntegralSet.Nothing;
            foreach(var alternative in alternatives)
            {
                if(Chain.ProjectIntervals(alternative.Facts, value, meter) is not { } projected)
                {
                    return new Alternative([], exhausted: true);
                }

                values = values.Or(projected);
            }

            facts.Add(Fact.In(value, values, merged: true));
        }

        return new Alternative(facts);
    }

    private bool Contradicted(Alternative alternative)
    {
        return alternative.Facts.Any(fact => fact.IsContradiction) || alternative.Facts.SelectMany(fact => fact.Term.Variables).Distinct()
            .Any(value => index.Bound(value).And(Chain.SingleValue(alternative.Facts, value)).IsEmpty);
    }

    private sealed record FactIndex(ImmutableDictionary<Value, ImmutableArray<int>> Mentions, ImmutableDictionary<Value, IntegralSet> Bounds, ImmutableArray<int> Constants)
    {
        public static FactIndex Of(ImmutableArray<Fact> common)
        {
            var mentions = new Dictionary<Value, List<int>>();
            var bounds = new Dictionary<Value, IntegralSet>();
            var constants = ImmutableArray.CreateBuilder<int>();
            for(var position = 0; position < common.Length; position++)
            {
                var fact = common[position];
                if(fact.Term.Variables.IsEmpty)
                {
                    constants.Add(position);
                }

                foreach(var value in fact.Term.Variables)
                {
                    if(!mentions.TryGetValue(value, out var positions))
                    {
                        positions = [];
                        mentions.Add(value, positions);
                    }

                    positions.Add(position);
                }

                if(fact.Term.Variables is [var single])
                {
                    bounds[single] = bounds.GetValueOrDefault(single, IntegralSet.Everything).And(Chain.SingleValue([fact], single));
                }
            }

            return new FactIndex(mentions.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutableArray()), bounds.ToImmutableDictionary(),
                constants.ToImmutable());
        }

        public ImmutableArray<int> Of(Value value)
        {
            return Mentions.GetValueOrDefault(value, []);
        }

        public IntegralSet Bound(Value value)
        {
            return Bounds.GetValueOrDefault(value) ?? IntegralSet.Everything;
        }

        public FactIndex With(Fact fact, int position)
        {
            var mentions = Mentions;
            foreach(var value in fact.Term.Variables)
            {
                mentions = mentions.SetItem(value, Of(value).Add(position));
            }

            return new FactIndex(mentions, fact.Term.Variables is [var single] ? Bounds.SetItem(single, Bound(single).And(Chain.SingleValue([fact], single))) : Bounds,
                fact.Term.Variables.IsEmpty ? Constants.Add(position) : Constants);
        }
    }
}