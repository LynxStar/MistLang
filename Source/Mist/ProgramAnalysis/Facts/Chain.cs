using System.Numerics;

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

                var projected = Project(bag.Common.Concat(alternative.Facts).Concat(broken.Facts), contractValues, meter);
                if(!seen.Add(projected))
                {
                    continue;
                }

                runs++;
                foreach(var expanded in Expand(projected.Facts, meter))
                {
                    var remaining = Remove(expanded, new HashSet<Value>(), meter);
                    if(meter.Expired)
                    {
                        return new Exhausted { Runs = runs };
                    }

                    if(!remaining.Any(fact => fact.IsContradiction))
                    {
                        var failing = Remove(expanded, contractValues, meter);
                        if(meter.Expired)
                        {
                            return new Exhausted { Runs = runs };
                        }

                        var afterMerge = failing.Any(fact => fact.IsMerged)
                            || projected.Facts.Any(fact => fact.IsMerged && fact.Values is not null);
                        return new Refuted(new Alternative(failing), afterMerge) { Runs = runs };
                    }
                }

                if(meter.Expired)
                {
                    return new Exhausted { Runs = runs };
                }
            }
        }

        return new Proven { Runs = runs };
    }

    internal static Alternative Project(IEnumerable<Fact> source, IReadOnlySet<Value> contractValues, ProofBudget meter)
    {
        var facts = source.ToList();
        var reachable = contractValues.ToHashSet();
        var changed = true;
        while(changed && !meter.Expired)
        {
            changed = false;
            foreach(var fact in facts)
            {
                if(fact.Term.Coefficients.Keys.Any(reachable.Contains))
                {
                    foreach(var value in fact.Term.Coefficients.Keys)
                    {
                        changed |= reachable.Add(value);
                    }
                }
            }
        }

        return new Alternative(facts.Where(fact => fact.Term.Coefficients.Count == 0 || fact.Term.Coefficients.Keys.Any(reachable.Contains)));
    }

    internal static IntegralSet? ProjectIntervals(IEnumerable<Fact> facts, Value value, ProofBudget meter)
    {
        var projected = Project(facts, new HashSet<Value> { value }, meter);
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

        return meter.Expired ? null : union;
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
            else if(fact.Values is null && fact.Term.Coefficients.Count == 1 && fact.Term.Coefficients.GetValueOrDefault(value) is var coefficient && !coefficient.IsZero)
            {
                var normalized = Integral(fact);
                coefficient = normalized.Term.Coefficients[value];
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
        return fact.Relation == Relation.LessThan && fact.Values is null
            ? new Fact(fact.Term.Add(new Term(1)), merged: fact.IsMerged) : fact;
    }

    private static List<Fact> Remove(IEnumerable<Fact> source, IReadOnlySet<Value> keep, ProofBudget meter)
    {
        var facts = Clean(source);
        while(!meter.Expired && !facts.Any(fact => fact.IsContradiction))
        {
            var value = facts.SelectMany(fact => fact.Term.Coefficients.Keys).Where(value => !keep.Contains(value))
                .GroupBy(value => value).OrderBy(group => group.Count()).ThenBy(group => group.Key.Id).Select(group => group.Key).FirstOrDefault();
            if(value is null)
            {
                break;
            }

            var involving = facts.Where(fact => fact.Term.Coefficients.ContainsKey(value)).ToList();
            var next = facts.Except(involving).ToList();
            var equality = involving.FirstOrDefault(fact => fact.Relation == Relation.Equal);
            if(equality is not null)
            {
                var coefficient = equality.Term.Coefficients[value];
                foreach(var fact in involving.Where(fact => !ReferenceEquals(fact, equality)))
                {
                    if(!meter.Derive())
                    {
                        return facts;
                    }

                    var term = fact.Term.Scale(BigInteger.Abs(coefficient))
                        .Subtract(equality.Term.Scale(fact.Term.Coefficients[value] * coefficient.Sign));
                    next.Add(new Fact(term, fact.Relation, fact.IsMerged || equality.IsMerged));
                }
            }
            else
            {
                foreach(var upper in involving.Where(fact => fact.Term.Coefficients[value].Sign > 0))
                {
                    foreach(var lower in involving.Where(fact => fact.Term.Coefficients[value].Sign < 0))
                    {
                        if(!meter.Derive())
                        {
                            return facts;
                        }

                        var term = upper.Term.Scale(-lower.Term.Coefficients[value]).Add(lower.Term.Scale(upper.Term.Coefficients[value]));
                        next.Add(new Fact(term, merged: upper.IsMerged || lower.IsMerged));
                    }
                }
            }

            facts = Clean(next);
        }

        return facts;
    }

    private static List<Fact> Clean(IEnumerable<Fact> source)
    {
        var facts = source.Distinct().ToList();
        var contradiction = facts.FirstOrDefault(fact => fact.IsContradiction);
        return contradiction is not null ? [contradiction] : facts.Where(fact => fact.Term.Coefficients.Count > 0).ToList();
    }
}