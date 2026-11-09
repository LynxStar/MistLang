using Mist.ProgramAnalysis.Facts;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class FactFlow
{
    private const int Rounds = 3;

    public FactState Widen(FactState previous, FactState joined, int round)
    {
        if(round <= Rounds || previous.Bag.Unreachable || joined.Bag.Unreachable)
        {
            return joined;
        }

        var stable = joined.Bindings.Order.Where(holder => ReferenceEquals(previous.Bindings.Get(holder), joined.Bindings.Get(holder)))
            .Select(holder => joined.Bindings.Values[holder]).ToHashSet();
        var changed = new HashSet<Value>();
        var bounds = new List<Fact>();
        var summary = joined;
        foreach(var holder in joined.Bindings.Order)
        {
            var value = joined.Bindings.Values[holder];
            if(previous.Bindings.Get(holder) is not { } before || ReferenceEquals(before, value))
            {
                continue;
            }

            if(!stable.Contains(before))
            {
                _ = changed.Add(before);
            }

            _ = changed.Add(value);
            var old = previous.Intervals(before);
            var next = joined.Intervals(value);
            if(!old.IsEmpty && !next.IsEmpty)
            {
                var type = IntegralTypes.Range(value.Type) ?? (value.Type.FullyQualifiedName == "Standard.Boolean"
                    ? new IntegralInterval(0, 1) : new IntegralInterval(null, null));
                var low = old.Intervals[0].Low == next.Intervals[0].Low ? next.Intervals[0].Low : type.Low;
                var high = old.Intervals[^1].High == next.Intervals[^1].High ? next.Intervals[^1].High : type.High;
                bounds.Add(Fact.In(value, new IntegralSet([new(low, high)])));
            }

            var known = joined.Details.GetValueOrDefault(value) ?? Knowledge.Unknown;
            var prior = previous.Details.GetValueOrDefault(before) ?? Knowledge.Unknown;
            if(known.Length is { } length && prior.Length is { } earlierLength)
            {
                known = known with { Length = length.WidenedAgainst(earlierLength) };
            }

            if(known.Real is { } real && prior.Real is { } earlier)
            {
                known = known with { Real = real.WidenedAgainst(earlier) };
            }

            summary = summary.SetItem(holder, known);
        }

        if(changed.Count == 0)
        {
            return joined;
        }

        // Keeping the previous path order makes widening discard facts without rotating alternatives when the join reaches its cap.
        var held = joined.Bag.Common.Concat(joined.Bag.Alternatives.SelectMany(alternative => alternative.Facts)).ToHashSet();
        bool keep(Fact fact)
        {
            return !fact.Term.Variables.Any(changed.Contains) && held.Contains(fact);
        }

        var common = previous.Bag.Common.Where(keep).ToList();
        var shared = common.ToHashSet();
        var meter = new ProofBudget(Budget.Default);
        var alternatives = previous.Bag.Alternatives.SelectMany(alternative =>
            Chain.Without(previous.Bag.Common.Concat(alternative.Facts).Where(held.Contains), changed, meter)
                .Select(projected => new Alternative(projected.Facts.Where(fact => !shared.Contains(fact)), projected.IsExhausted || alternative.IsExhausted)));
        return summary with { Bag = new Bag(common.Concat(bounds), alternatives, alternativeCap: alternativeCap) };
    }
}