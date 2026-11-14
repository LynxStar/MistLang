using System.Collections.Immutable;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class FactFlow
{
    public FactState Join(FactState left, FactState right)
    {
        if(left.Bag.Unreachable || right.Bag.Unreachable)
        {
            return left.Bag.Unreachable ? right : left;
        }

        if(ReferenceEquals(left, right))
        {
            return left;
        }

        var joined = Nothing with
        {
            Unseen = unseenWrites,
            PatternSources = left.PatternSources,
            StableHolders = left.StableHolders.Intersect(right.StableHolders),
            CompoundInputs = Agreeing(left.CompoundInputs, right.CompoundInputs),
            Results = Agreeing(left.Results, right.Results),
        };
        var mine = new List<Fact>();
        var theirs = new List<Fact>();
        var pairs = new Dictionary<(Value, Value), Value>();
        foreach(var holder in left.Bindings.Order)
        {
            var before = left.Bindings.Values[holder];
            if(right.Bindings.Get(holder) is not { } after)
            {
                continue;
            }

            if(!pairs.TryGetValue((before, after), out var value))
            {
                value = ReferenceEquals(before, after) ? before : new Value(before.Type, before.DisplayName, before.Node);
                pairs.Add((before, after), value);
                if(!ReferenceEquals(before, after))
                {
                    mine.Add(new Fact(new Term(value).Subtract(new Term(before)), Relation.Equal));
                    theirs.Add(new Fact(new Term(value).Subtract(new Term(after)), Relation.Equal));
                }
            }

            joined = joined with { Bindings = joined.Bindings.Set(holder, value) };
            joined = joined.SetItem(holder, JoinKnowledge(left.Details.GetValueOrDefault(before) ?? Knowledge.Unknown,
                right.Details.GetValueOrDefault(after) ?? Knowledge.Unknown));
        }

        joined = joined with { Details = Carried(left, right, Carried(right, left, joined.Details)) };
        var live = joined.Bindings.Order.Select(holder => joined.Bindings.Values[holder])
            .Concat(joined.Results.Values.SelectMany(term => term.Variables))
            .Concat(joined.CompoundInputs.Values.SelectMany(term => term.Variables));
        return joined with { Bag = left.Bag.Join(right.Bag, mine, theirs, alternativeCap, live) };
    }

    // Text facts hold in every alternative that knows the value. An alternative that never sees it
    // cannot contradict them; retaining them preserves knowledge when that alternative is selected later.
    private static ImmutableDictionary<Value, Knowledge> Carried(FactState side, FactState other, ImmutableDictionary<Value, Knowledge> details)
    {
        foreach(var (value, known) in side.Details)
        {
            if(details.ContainsKey(value) || (known.Literal is null && known.Matches.IsEmpty)
                || (!IsString(value.Type) && Requirement.Unrefined(value.Type).FullyQualifiedName != WellKnownTypes.Regex.FullyQualifiedName))
            {
                continue;
            }

            var text = Knowledge.Unknown with { Literal = known.Literal, Matches = known.Matches };
            if(other.Details.TryGetValue(value, out var theirs) ? Equals(theirs.Literal, known.Literal) && theirs.Matches.SetEquals(known.Matches) : !Mentions(other, value))
            {
                details = details.SetItem(value, text);
            }
        }

        return details;
    }

    private static bool Mentions(FactState state, Value value)
    {
        return state.Bindings.Values.ContainsValue(value) || state.Results.Values.Any(term => term.Variables.Contains(value))
            || state.CompoundInputs.Values.Any(term => term.Variables.Contains(value))
            || state.Bag.Common.Concat(state.Bag.Alternatives.SelectMany(alternative => alternative.Facts)).Any(fact => fact.Term.Variables.Contains(value));
    }

    private static Knowledge JoinKnowledge(Knowledge left, Knowledge right)
    {
        return new Knowledge(left.Values.Or(right.Values), left.Bounds.Intersect(right.Bounds),
            left.Length is { } a && right.Length is { } b ? a.Or(b) : null,
            Equals(left.Slice, right.Slice) ? left.Slice : null,
            left.RefusedSlice && right.RefusedSlice,
            Equals(left.Literal, right.Literal) ? left.Literal : null,
            Equals(left.IndexValue, right.IndexValue) ? left.IndexValue : null,
            left.Real is { } mine && right.Real is { } theirs && mine.Kind == theirs.Kind ? mine.Or(theirs) : null)
        {
            Matches = left.Matches.Intersect(right.Matches),
        };
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
}