using Mist.ProgramAnalysis.Facts;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class FactFlow
{
    public bool Same(FactState left, FactState right)
    {
        if(ReferenceEquals(left, right))
        {
            return true;
        }

        if(left.Bag.Unreachable || right.Bag.Unreachable)
        {
            return left.Bag.Unreachable == right.Bag.Unreachable;
        }

        if(left.Bindings.Values.Count != right.Bindings.Values.Count || left.Results.Count != right.Results.Count
            || left.CompoundInputs.Count != right.CompoundInputs.Count || left.Bag.Common.Length != right.Bag.Common.Length
            || left.Bag.Alternatives.Length != right.Bag.Alternatives.Length)
        {
            return false;
        }

        var forward = new Dictionary<Value, Value>();
        var backward = new Dictionary<Value, Value>();
        bool pair(Value mine, Value theirs)
        {
            if((forward.TryGetValue(mine, out var mapped) && !ReferenceEquals(mapped, theirs))
                || (backward.TryGetValue(theirs, out var reverse) && !ReferenceEquals(reverse, mine)))
            {
                return false;
            }

            forward[mine] = theirs;
            backward[theirs] = mine;
            return ReferenceEquals(mine.Type, theirs.Type);
        }

        foreach(var holder in left.Bindings.Order)
        {
            var mine = left.Bindings.Values[holder];
            if(right.Bindings.Get(holder) is not { } theirs
                || ((left.StableHolders.Contains(holder) || right.StableHolders.Contains(holder)) && !ReferenceEquals(mine, theirs)) || !pair(mine, theirs))
            {
                return false;
            }

            var a = left.Details.GetValueOrDefault(mine) ?? Knowledge.Unknown;
            var b = right.Details.GetValueOrDefault(theirs) ?? Knowledge.Unknown;
            if(!a.Values.Equals(b.Values) || !a.Bounds.SetEquals(b.Bounds) || !Equals(a.Length, b.Length) || !Equals(a.Slice, b.Slice)
                || a.RefusedSlice != b.RefusedSlice || !Equals(a.Literal, b.Literal) || !Equals(a.IndexValue, b.IndexValue) || !Equals(a.Real, b.Real) || !a.Matches.SetEquals(b.Matches)
                || !a.Texts.SequenceEqual(b.Texts))
            {
                return false;
            }
        }

        bool term(Term mine, Term theirs)
        {
            if(mine.Constant != theirs.Constant || mine.Variables.Length != theirs.Variables.Length)
            {
                return false;
            }

            for(var i = 0; i < mine.Variables.Length; i++)
            {
                var value = mine.Variables[i];
                var other = forward.GetValueOrDefault(value) ?? theirs.Variables[i];
                if(!pair(value, other) || mine.Multipliers[i] != theirs.Multiplier(other))
                {
                    return false;
                }
            }

            return true;
        }

        bool facts(IReadOnlyList<Fact> mine, IReadOnlyList<Fact> theirs)
        {
            if(mine.Count != theirs.Count)
            {
                return false;
            }

            for(var i = 0; i < mine.Count; i++)
            {
                if(mine[i].Relation != theirs[i].Relation || mine[i].IsMerged != theirs[i].IsMerged
                    || !Equals(mine[i].Values, theirs[i].Values) || !term(mine[i].Term, theirs[i].Term))
                {
                    return false;
                }
            }

            return true;
        }

        if(!facts(left.Bag.Common, right.Bag.Common))
        {
            return false;
        }

        for(var i = 0; i < left.Bag.Alternatives.Length; i++)
        {
            var mine = left.Bag.Alternatives[i];
            var theirs = right.Bag.Alternatives[i];
            if(mine.IsExhausted != theirs.IsExhausted || !facts(mine.Facts, theirs.Facts))
            {
                return false;
            }
        }

        return !left.Results.Any(entry => !right.Results.TryGetValue(entry.Key, out var other) || !term(entry.Value, other))
            && !left.CompoundInputs.Any(entry => !right.CompoundInputs.TryGetValue(entry.Key, out var other) || !term(entry.Value, other));
    }
}