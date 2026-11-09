using System.Collections.Immutable;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Flow;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class FactFlow
{
    private const int Rounds = 3;

    public FactState PrepareWiden(FactState joined, FactState incoming, FlowBlock header)
    {
        var changes = loopChanges.GetValueOrDefault(header);
        return joined with
        {
            WideningEntry = incoming,
            WideningHeader = header,
            StableHolders = incoming.Bindings.Order.Where(holder => changes is not null && !changes.Writes(holder, incoming, reading, effects)).ToImmutableHashSet(),
        };
    }

    public FactState Widen(FactState previous, FactState joined, int round)
    {
        if(round <= Rounds || previous.Bag.Unreachable || joined.Bag.Unreachable)
        {
            return joined;
        }

        var source = joined.WideningEntry ?? previous;
        var changes = joined.WideningHeader is { } header ? loopChanges.GetValueOrDefault(header) : null;
        var stable = joined.Bindings.Order.Where(holder => changes is not null
            ? !changes.Writes(holder, source, reading, effects)
            : ReferenceEquals(previous.Bindings.Get(holder), joined.Bindings.Get(holder))).ToHashSet();
        var protectedValues = stable.Select(source.Bindings.Get).OfType<Value>().ToHashSet();
        var removed = new HashSet<Value>();
        var bounds = new List<Fact>();
        // Loop-entry facts anchor values the loop does not write; replaying prior iterations would summarize outer counters in an inner loop.
        var summary = joined with { Bindings = source.Bindings, Details = source.Details, Ranges = source.Ranges };
        foreach(var holder in joined.Bindings.Order)
        {
            if(stable.Contains(holder))
            {
                continue;
            }

            var value = joined.Bindings.Values[holder];
            var before = previous.Bindings.Get(holder);
            if(before is not null && !protectedValues.Contains(before))
            {
                _ = removed.Add(before);
            }

            if(!protectedValues.Contains(value))
            {
                _ = removed.Add(value);
            }

            summary = summary with { Bindings = summary.Bindings.Set(holder, value) };
            var old = before is not null ? previous.Intervals(before) : IntegralSet.Everything;
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
            var prior = before is not null ? previous.Details.GetValueOrDefault(before) ?? Knowledge.Unknown : Knowledge.Unknown;
            if(known.Real is { } real && prior.Real is { } earlier)
            {
                known = known with { Real = real.WidenedAgainst(earlier) };
            }

            if(known.Length is { } length && prior.Length is { } earlierLength)
            {
                known = known with { Length = length.WidenedAgainst(earlierLength) };
            }

            summary = summary.SetItem(holder, known);
        }

        var common = source.Bag.Common.Where(fact => !fact.Term.Variables.Any(removed.Contains)).ToList();
        var shared = common.ToHashSet();
        var meter = new ProofBudget(Budget.Default);
        var alternatives = source.Bag.Alternatives.SelectMany(alternative =>
            Chain.Without(source.Bag.Common.Concat(alternative.Facts), removed, meter)
                .Select(projected => new Alternative(projected.Facts.Where(fact => !shared.Contains(fact)), projected.IsExhausted || alternative.IsExhausted)));
        return summary with { Bag = new Bag(common.Concat(bounds), alternatives, alternativeCap: alternativeCap) };
    }
}