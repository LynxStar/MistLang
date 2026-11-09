using System.Collections.Immutable;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class FactFlow
{
    public FactState Join(FactState left, FactState right)
    {
        if(left.Bag.Unreachable || right.Bag.Unreachable)
        {
            return left.Bag.Unreachable ? right : left;
        }

        var inputs = Agreeing(left.CompoundInputs, right.CompoundInputs);
        var results = Agreeing(left.Results, right.Results);
        var common = left.Bag.Shared(right.Bag);
        var joined = Nothing with { Unseen = unseenWrites, IsSummary = true, PatternSources = left.PatternSources, CompoundInputs = inputs, Results = results, Bag = common };
        foreach(var (holder, mine) in left.Bindings.Values)
        {
            if(right.Bindings.Get(holder) is not { } theirs)
            {
                continue;
            }

            var before = left.Intervals(mine);
            var values = before.Or(right.Intervals(theirs));
            var known = left.Details.GetValueOrDefault(mine) ?? Knowledge.Unknown;
            var other = right.Details.GetValueOrDefault(theirs) ?? Knowledge.Unknown;
            var changes = known.Changes;
            var lowerChanges = known.LowerChanges;
            var upperChanges = known.UpperChanges;
            if(!values.Equals(before))
            {
                changes++;
                if(values.Intervals.Length > 0 && before.Intervals.Length > 0)
                {
                    var low = values.Intervals[0].Low;
                    var high = values.Intervals[^1].High;
                    var widened = false;
                    if(low != before.Intervals[0].Low)
                    {
                        lowerChanges++;
                        if(lowerChanges > 2)
                        {
                            low = null;
                            widened = true;
                        }
                    }

                    if(high != before.Intervals[^1].High)
                    {
                        upperChanges++;
                        if(upperChanges > 2)
                        {
                            high = null;
                            widened = true;
                        }
                    }

                    if(widened || (changes > 2 && values.Intervals.Length > 1))
                    {
                        values = new IntegralSet([new(low, high)]);
                    }
                }
            }

            var real = known.Real is { } leftReal && other.Real is { } rightReal && leftReal.Kind == rightReal.Kind ? leftReal.Or(rightReal) : null;
            var realChanges = known.RealChanges;
            if(real is not null && known.Real is { } previousReal && !real.Equals(previousReal))
            {
                realChanges++;
                real = realChanges > 2 ? real.WidenedAgainst(previousReal) : real;
            }

            var merged = new Knowledge(FactState.AdmittedValues(values), known.Bounds.IsEmpty ? known.Bounds : known.Bounds.Intersect(other.Bounds), changes,
                known.Length is { } a && other.Length is { } b ? a.Or(b) : null,
                Equals(known.Slice, other.Slice) ? known.Slice : null,
                known.RefusedSlice && other.RefusedSlice,
                Equals(known.Literal, other.Literal) ? known.Literal : null,
                Equals(known.IndexValue, other.IndexValue) ? known.IndexValue : null, real, realChanges, lowerChanges, upperChanges);
            var value = ReferenceEquals(mine, theirs) ? mine : FactReading.Fresh(ref joined, mine.Type, mine.DisplayName, mine.Node);
            joined = joined with { Bindings = joined.Bindings.Set(holder, value), Bag = joined.Bag.Add(Fact.In(value, values, merged: true)) };
            joined = joined.SetItem(holder, merged);
            foreach(var bound in merged.Bounds)
            {
                if(bound.Domain is { High: { } high } && bound.Kind is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual)
                {
                    var strict = bound.Kind == ComparisonKind.LessThan ? 1 : 0;
                    joined = joined with { Bag = joined.Bag.Add(new Fact(new Term(value).Add(new Term(strict - high)))) };
                }
            }
        }

        return joined;
    }

    private static ImmutableDictionary<TKey, Term> Agreeing<TKey>(ImmutableDictionary<TKey, Term> left, ImmutableDictionary<TKey, Term> right)
        where TKey : notnull
    {
        if(ReferenceEquals(left, right))
        {
            return left;
        }

        var differing = left.Where(pair => !right.TryGetValue(pair.Key, out var term) || !pair.Value.Equals(term)).Select(pair => pair.Key).ToList();
        return differing.Count == 0 ? left : left.RemoveRange(differing);
    }

    private static bool SameAliasing(Bindings left, Bindings right)
    {
        var forward = new Dictionary<Value, Value>();
        var backward = new Dictionary<Value, Value>();
        foreach(var (holder, mine) in left.Values)
        {
            if(right.Get(holder) is not { } theirs || (forward.TryGetValue(mine, out var paired) && !ReferenceEquals(paired, theirs))
                || (backward.TryGetValue(theirs, out var reverse) && !ReferenceEquals(reverse, mine)))
            {
                return false;
            }

            forward[mine] = theirs;
            backward[theirs] = mine;
        }

        return true;
    }

    public bool Same(FactState left, FactState right)
    {
        if(left.Bag.Unreachable || right.Bag.Unreachable)
        {
            return left.Bag.Unreachable == right.Bag.Unreachable;
        }

        if(left.IsSummary != right.IsSummary || left.Bindings.Values.Count != right.Bindings.Values.Count
            || left.Results.Count != right.Results.Count || left.Results.Any(pair => !right.Results.TryGetValue(pair.Key, out var term) || !pair.Value.Equals(term))
            || left.CompoundInputs.Count != right.CompoundInputs.Count || left.CompoundInputs.Any(pair => !right.CompoundInputs.TryGetValue(pair.Key, out var term) || !pair.Value.Equals(term)))
        {
            return false;
        }

        var relations = left.Bag.Common.Where(fact => fact.Term.Variables.Length > 1).ToHashSet();
        if(!relations.SetEquals(right.Bag.Common.Where(fact => fact.Term.Variables.Length > 1)))
        {
            return false;
        }

        if(!SameAliasing(left.Bindings, right.Bindings))
        {
            return false;
        }

        foreach(var (holder, value) in left.Bindings.Values)
        {
            if(right.Bindings.Get(holder) is not { } other || !left.Intervals(value).Equals(right.Intervals(other)))
            {
                return false;
            }

            var mine = left.Details.GetValueOrDefault(value) ?? Knowledge.Unknown;
            var theirs = right.Details.GetValueOrDefault(other) ?? Knowledge.Unknown;
            if(!mine.Bounds.SetEquals(theirs.Bounds) || !Equals(mine.Length, theirs.Length) || !Equals(mine.Slice, theirs.Slice)
                || mine.RefusedSlice != theirs.RefusedSlice || !Equals(mine.Literal, theirs.Literal)
                || !Equals(mine.IndexValue, theirs.IndexValue) || !Equals(mine.Real, theirs.Real))
            {
                return false;
            }
        }

        return true;
    }
}