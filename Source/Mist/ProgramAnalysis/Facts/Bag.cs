using System.Collections.Immutable;

namespace Mist.ProgramAnalysis.Facts;

internal sealed class Bag
{
    public const int AlternativeCap = 16;
    public static readonly Bag Empty = new([], [Alternative.Empty]);

    public Bag(IEnumerable<Fact> common, IEnumerable<Alternative> alternatives, Budget? mergeBudget = null)
    {
        Common = [.. common.Distinct()];
        var viable = alternatives.Where(alternative => !Contradicted(Common.Concat(alternative.Facts))).ToList();
        if(viable.Count > AlternativeCap)
        {
            var count = viable.Count - AlternativeCap + 1;
            var merged = MergePerVariable(viable.Take(count).Select(alternative => alternative.Add(Common)), mergeBudget);
            viable = [merged, .. viable.Skip(count)];
        }

        Unreachable = viable.Count == 0;
        Alternatives = Unreachable ? [Alternative.Empty] : [.. viable];
    }

    public ImmutableArray<Fact> Common { get; }
    public ImmutableArray<Alternative> Alternatives { get; }
    public bool Unreachable { get; }

    public Bag Add(Fact fact)
    {
        return new Bag(Common.Add(fact), Unreachable ? [] : Alternatives);
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
        foreach(var value in alternatives.SelectMany(alternative => alternative.Facts).SelectMany(fact => fact.Term.Coefficients.Keys).Distinct())
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

    private static bool Contradicted(IEnumerable<Fact> source)
    {
        var facts = source.ToList();
        return facts.Any(fact => fact.IsContradiction)
            || facts.SelectMany(fact => fact.Term.Coefficients.Keys).Distinct().Any(value => Chain.SingleValue(facts, value).IsEmpty);
    }
}