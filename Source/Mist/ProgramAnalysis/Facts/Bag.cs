using System.Collections.Immutable;

namespace Mist.ProgramAnalysis.Facts;

internal sealed class Bag
{
    public const int AlternativeCap = 16;
    public static readonly Bag Empty = new([], [Alternative.Empty]);

    public Bag(IEnumerable<Fact> common, IEnumerable<Alternative> alternatives, Budget? mergeBudget = null, int alternativeCap = AlternativeCap)
        : this([.. common.Distinct()], alternatives, mergeBudget, parent: null, alternativeCap)
    {
    }

    private Bag(ImmutableArray<Fact> common, IEnumerable<Alternative> alternatives, Budget? mergeBudget, Bag? parent, int alternativeCap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(alternativeCap, 1);
        this.alternativeCap = alternativeCap;
        Common = common;
        index = parent?.index ?? FactIndex.Of(Common);
        this.parent = parent;
        var contradicted = Common.Any(fact => fact.IsContradiction) || index.Bounds.Values.Any(values => values.IsEmpty);
        var viable = contradicted ? [] : alternatives.Where(alternative => !Contradicted(alternative)).Distinct().ToList();
        if(viable.Count > alternativeCap)
        {
            var count = viable.Count - alternativeCap + 1;
            var merged = MergePerVariable(viable.Take(count).Select(alternative => alternative.Add(Common)), mergeBudget);
            viable = [merged, .. viable.Skip(count)];
        }

        Unreachable = viable.Count == 0;
        Alternatives = Unreachable ? [Alternative.Empty] : [.. viable];
    }

    private Bag(ImmutableArray<Fact> common, ImmutableArray<Alternative> alternatives, FactIndex index, ImmutableDictionary<Value, IntegralSet> projections, Bag parent)
    {
        Common = common;
        Unreachable = alternatives.IsEmpty;
        Alternatives = Unreachable ? [Alternative.Empty] : alternatives;
        this.index = index;
        this.projections = Unreachable ? ImmutableDictionary<Value, IntegralSet>.Empty : projections;
        this.parent = parent;
        alternativeCap = parent.alternativeCap;
    }

    private readonly int alternativeCap;
    private readonly FactIndex index;
    private readonly Bag? parent;
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
        return new Bag(common, viable, indexed, kept, this);
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
        return new Bag(Common, Unreachable ? [] : Alternatives.SelectMany(existing => added.Select(next => existing.Add(next.Facts, next.IsExhausted))), null, this, alternativeCap);
    }

    /// <summary>
    /// The facts of <see cref="Common"/> that <paramref name="other"/> also holds, in this bag's order, with no alternatives. The
    /// longest prefix of them is a bag this one was extended from, so its index and projections are reused and only the shared
    /// facts after it are added, rather than indexing every shared fact again.
    /// </summary>
    public Bag Shared(Bag other)
    {
        var kept = 0;
        while(kept < Common.Length && other.Holds(Common[kept]))
        {
            kept++;
        }

        var shared = Ancestor(kept);
        for(var position = kept + 1; position < Common.Length; position++)
        {
            if(other.Holds(Common[position]))
            {
                shared = shared.Add(Common[position]);
            }
        }

        return shared;
    }

    private Bag Ancestor(int length)
    {
        var ancestor = this;
        while(ancestor.Common.Length > length && ancestor.parent is { } parent)
        {
            ancestor = parent;
        }

        if(ancestor.Common.Length != length)
        {
            return new Bag(Common.Take(length), [Alternative.Empty]);
        }

        if(ancestor.Alternatives is [{ IsExhausted: false, Facts.IsEmpty: true }] && !ancestor.Unreachable)
        {
            return ancestor;
        }

        return new Bag(ancestor.Common, [Alternative.Empty], ancestor.index, ImmutableDictionary<Value, IntegralSet>.Empty, ancestor);
    }

    public Bag Join(Bag other)
    {
        if(Unreachable || other.Unreachable)
        {
            return Unreachable ? other : this;
        }

        return Join(other, [], [], alternativeCap);
    }

    public Bag WithCap(int cap)
    {
        return cap == alternativeCap ? this : new Bag(Common, Unreachable ? [] : Alternatives, null, this, cap);
    }

    public Bag Join(Bag other, IEnumerable<Fact> leftFacts, IEnumerable<Fact> rightFacts, int cap)
    {
        if(Unreachable || other.Unreachable)
        {
            return Unreachable ? other : this;
        }

        var common = Shared(other);
        var mine = Common.Where(fact => !common.Holds(fact)).Concat(leftFacts).ToList();
        var theirs = other.Common.Where(fact => !common.Holds(fact)).Concat(rightFacts).ToList();
        var alternatives = Alternatives.Select(alternative => alternative.Add(mine))
            .Concat(other.Alternatives.Select(alternative => alternative.Add(theirs))).Distinct().ToImmutableArray();
        return alternatives is [{ IsExhausted: false, Facts.IsEmpty: true }] ? common.WithCap(cap)
            : new Bag(common.Common, alternatives, null, common, cap);
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