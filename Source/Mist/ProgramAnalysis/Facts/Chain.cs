using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mist.ProgramAnalysis.Facts;

internal abstract record Proof
{
    public int Runs { get; init; }
}

internal sealed record Proven : Proof;
internal sealed record Refuted(Alternative FailingCase, bool AfterMerge) : Proof;
internal sealed record Exhausted : Proof;

internal static class Chain
{
    // Each memo is keyed by everything its work reads, the projected facts in order, and a hit charges the meter the facts the first
    // run derived: a remembered answer exhausts a budget exactly when recomputing it would.
    private static readonly ConditionalWeakTable<Value, ConcurrentDictionary<ImmutableArray<Fact>, (IntegralSet Values, int Derived)>> Projections = new();
    private static readonly ConditionalWeakTable<Value, ConcurrentDictionary<PairKey, (Proof? Proof, int Derived)>> Pairs = new();

    public static Proof Prove(Bag bag, IReadOnlyList<Alternative> denial, IReadOnlySet<Value> contractValues, Budget? budget = null)
    {
        if(bag.Unreachable)
        {
            return new Proven();
        }

        var meter = new ProofBudget(budget ?? Budget.Default);
        var seen = new HashSet<Alternative>();
        var runs = 0;
        foreach(var alternative in bag.Alternatives)
        {
            foreach(var broken in denial)
            {
                if(meter.Expired || alternative.IsExhausted || broken.IsExhausted)
                {
                    return new Exhausted { Runs = runs };
                }

                var projected = Project(bag.Relevant(contractValues, alternative.Facts.Concat(broken.Facts)), contractValues, meter);
                if(!seen.Add(projected))
                {
                    continue;
                }

                runs++;
                if(Pair(projected, contractValues, meter) is { } unproven)
                {
                    return unproven with { Runs = runs };
                }
            }
        }

        return new Proven { Runs = runs };
    }

    internal static IEnumerable<Alternative> Without(IEnumerable<Fact> source, IReadOnlySet<Value> removed, ProofBudget meter)
    {
        var facts = source.ToList();
        var affected = facts.Where(fact => fact.Term.Variables.Any(removed.Contains)).ToList();
        var untouched = facts.Where(fact => !fact.Term.Variables.Any(removed.Contains)).ToList();
        var keep = affected.SelectMany(fact => fact.Term.Variables).Where(value => !removed.Contains(value)).ToHashSet();
        foreach(var expanded in Expand(affected, meter))
        {
            var projected = Remove(expanded, keep, meter);
            if(meter.Expired)
            {
                break;
            }

            yield return new Alternative(untouched.Concat(projected));
        }

        if(meter.Expired)
        {
            yield return new Alternative([], exhausted: true);
        }
    }

    private static Proof? Pair(Alternative projected, IReadOnlySet<Value> contractValues, ProofBudget meter)
    {
        var contract = contractValues.OrderBy(value => value.Id).ToImmutableArray();
        var known = contract.IsEmpty ? null : Pairs.GetValue(contract[0], _ => new());
        var key = new PairKey(projected.Facts, contract);
        if(known is not null && known.TryGetValue(key, out var memo))
        {
            return !meter.Derive(memo.Derived) ? new Exhausted() : memo.Proof;
        }

        var start = meter.Derived;
        var proof = Judged(projected, contractValues, meter);
        if(known is not null && proof is not Exhausted)
        {
            known[key] = (proof, meter.Derived - start);
        }

        return proof;
    }

    private static Proof? Judged(Alternative projected, IReadOnlySet<Value> contractValues, ProofBudget meter)
    {
        foreach(var expanded in Expand(projected.Facts, meter))
        {
            var remaining = Remove(expanded, new HashSet<Value>(), meter);
            if(meter.Expired)
            {
                return new Exhausted();
            }

            if(!remaining.Any(fact => fact.IsContradiction))
            {
                var failing = Remove(expanded, contractValues, meter);
                if(meter.Expired)
                {
                    return new Exhausted();
                }

                var afterMerge = failing.Any(fact => fact.IsMerged)
                    || projected.Facts.Any(fact => fact.IsMerged && fact.Values is not null);
                return new Refuted(new Alternative(failing), afterMerge);
            }
        }

        return meter.Expired ? new Exhausted() : null;
    }

    internal static Alternative Project(IEnumerable<Fact> source, IReadOnlySet<Value> contractValues, ProofBudget meter)
    {
        var facts = source.ToList();
        var reachable = contractValues.ToHashSet();
        var linked = new bool[facts.Count];
        var changed = true;
        while(changed && !meter.Expired)
        {
            changed = false;
            for(var i = 0; i < facts.Count; i++)
            {
                if(!linked[i] && Mentions(facts[i], reachable))
                {
                    linked[i] = true;
                    foreach(var value in facts[i].Term.Variables)
                    {
                        changed |= reachable.Add(value);
                    }
                }
            }
        }

        return new Alternative(facts.Where(fact => fact.Term.Variables.IsEmpty || Mentions(fact, reachable)));
    }

    internal static IntegralSet? ProjectIntervals(IEnumerable<Fact> facts, Value value, ProofBudget meter)
    {
        var projected = Project(facts, new HashSet<Value> { value }, meter);
        var known = Projections.GetValue(value, _ => new(InOrder.Instance));
        if(known.TryGetValue(projected.Facts, out var memo))
        {
            return meter.Derive(memo.Derived) ? memo.Values : null;
        }

        var start = meter.Derived;
        var union = IntegralSet.Nothing;
        foreach(var expanded in Expand(projected.Facts, meter))
        {
            var remaining = Remove(expanded, new HashSet<Value> { value }, meter);
            if(meter.Expired)
            {
                return null;
            }

            union = union.Or(SingleValue(remaining, value));
        }

        if(meter.Expired)
        {
            return null;
        }

        known[projected.Facts] = (union, meter.Derived - start);
        return union;
    }

    private static bool Mentions(Fact fact, HashSet<Value> values)
    {
        foreach(var value in fact.Term.Variables)
        {
            if(values.Contains(value))
            {
                return true;
            }
        }

        return false;
    }

    internal static IntegralSet SingleValue(IEnumerable<Fact> facts, Value value)
    {
        var result = IntegralSet.Everything;
        foreach(var fact in facts)
        {
            if(fact.IsContradiction)
            {
                return IntegralSet.Nothing;
            }

            if(fact.Subject == value && fact.Values is { } values)
            {
                result = result.And(values);
            }
            else if(fact.Values is null && !fact.IsReal && fact.Term.Variables is [var only] && only == value)
            {
                var normalized = Integral(fact);
                var coefficient = normalized.Term.Multiplier(value);
                var bound = -normalized.Term.Constant / coefficient;
                result = result.And(new IntegralSet([normalized.Relation == Relation.Equal ? new(bound, bound)
                    : coefficient.Sign > 0 ? new(null, bound) : new(bound, null)]));
            }
        }

        return result;
    }

    private static IEnumerable<List<Fact>> Expand(IEnumerable<Fact> source, ProofBudget meter)
    {
        var pending = new Stack<(List<Fact> Facts, int Interval)>();
        pending.Push((source.ToList(), 0));
        while(pending.TryPop(out var current) && !meter.Expired)
        {
            var membership = current.Facts.FirstOrDefault(fact => fact.Values is not null);
            if(membership is null)
            {
                yield return current.Facts.Select(Integral).ToList();
                continue;
            }

            var intervals = membership.Values!.Intervals;
            if(current.Interval >= intervals.Length)
            {
                continue;
            }

            if(!meter.Derive())
            {
                yield break;
            }

            pending.Push((current.Facts, current.Interval + 1));
            var interval = intervals[current.Interval];
            var bounds = current.Facts.Where(fact => !ReferenceEquals(fact, membership)).ToList();
            var term = new Term(membership.Subject!);
            if(interval.Low is { } low)
            {
                bounds.Add(new Fact(term.Scale(-1).Add(new Term(low)), merged: membership.IsMerged));
            }

            if(interval.High is { } high)
            {
                bounds.Add(new Fact(term.Add(new Term(-high)), merged: membership.IsMerged));
            }

            pending.Push((bounds, 0));
        }
    }

    private static Fact Integral(Fact fact)
    {
        return fact.Relation == Relation.LessThan && fact.Values is null && !fact.IsReal
            ? new Fact(fact.Term.Add(new Term(1)), merged: fact.IsMerged) : fact;
    }

    private static List<Fact> Remove(IEnumerable<Fact> source, IReadOnlySet<Value> keep, ProofBudget meter)
    {
        var facts = Clean(source);
        while(!meter.Expired && !facts.Any(fact => fact.IsContradiction))
        {
            var value = Rarest(facts, keep);
            if(value is null || Eliminate(facts, value, meter) is not { } next)
            {
                break;
            }

            facts = next;
        }

        return facts;
    }

    internal static List<Fact>? Eliminate(List<Fact> facts, Value value, ProofBudget meter)
    {
        var involving = facts.Where(fact => fact.Term.Variables.Contains(value)).ToList();
        var next = facts.Where(fact => !fact.Term.Variables.Contains(value)).ToList();
        var equality = involving.FirstOrDefault(fact => fact.Relation == Relation.Equal);
        if(equality is not null)
        {
            var coefficient = equality.Term.Multiplier(value);
            foreach(var fact in involving.Where(fact => !ReferenceEquals(fact, equality)))
            {
                if(!meter.Derive())
                {
                    return null;
                }

                var term = fact.Term.Scale(BigInteger.Abs(coefficient))
                    .Subtract(equality.Term.Scale(fact.Term.Multiplier(value) * coefficient.Sign));
                next.Add(new Fact(term, fact.Relation, fact.IsMerged || equality.IsMerged));
            }
        }
        else
        {
            foreach(var upper in involving.Where(fact => fact.Term.Multiplier(value).Sign > 0))
            {
                foreach(var lower in involving.Where(fact => fact.Term.Multiplier(value).Sign < 0))
                {
                    if(!meter.Derive())
                    {
                        return null;
                    }

                    var term = upper.Term.Scale(-lower.Term.Multiplier(value)).Add(lower.Term.Scale(upper.Term.Multiplier(value)));
                    var strict = upper.Relation == Relation.LessThan || lower.Relation == Relation.LessThan;
                    next.Add(new Fact(term, strict ? Relation.LessThan : Relation.LessThanOrEqual, upper.IsMerged || lower.IsMerged));
                }
            }
        }

        return Clean(next);
    }

    private static Value? Rarest(List<Fact> facts, IReadOnlySet<Value> keep)
    {
        var mentions = new Dictionary<Value, int>();
        foreach(var fact in facts)
        {
            foreach(var value in fact.Term.Variables)
            {
                if(!keep.Contains(value))
                {
                    mentions[value] = mentions.GetValueOrDefault(value) + 1;
                }
            }
        }

        Value? rarest = null;
        foreach(var (value, count) in mentions)
        {
            if(rarest is null || count < mentions[rarest] || (count == mentions[rarest] && value.Id < rarest.Id))
            {
                rarest = value;
            }
        }

        return rarest;
    }

    internal static List<Fact> Clean(IEnumerable<Fact> source)
    {
        var facts = source.Distinct().ToList();
        var contradiction = facts.FirstOrDefault(fact => fact.IsContradiction);
        return contradiction is not null ? [contradiction] : facts.Where(fact => !fact.Term.Variables.IsEmpty).ToList();
    }

    private sealed class InOrder : IEqualityComparer<ImmutableArray<Fact>>
    {
        public static readonly InOrder Instance = new();

        public bool Equals(ImmutableArray<Fact> left, ImmutableArray<Fact> right)
        {
            return left.SequenceEqual(right);
        }

        public int GetHashCode(ImmutableArray<Fact> facts)
        {
            var hash = new HashCode();
            foreach(var fact in facts)
            {
                hash.Add(fact);
            }

            return hash.ToHashCode();
        }
    }

    private sealed record PairKey(ImmutableArray<Fact> Facts, ImmutableArray<Value> Contract)
    {
        public bool Equals(PairKey? other)
        {
            return other is not null && Facts.SequenceEqual(other.Facts) && Contract.SequenceEqual(other.Contract);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(InOrder.Instance.GetHashCode(Facts), Contract.Length);
        }
    }
}