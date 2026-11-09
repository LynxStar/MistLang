using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class BagTests
{
    [Fact]
    public void AddingAlternativesPairsEveryExistingPath()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var bag = new Bag([], [new Alternative([Eq(-1, (a, 1))]), new Alternative([Eq(-2, (a, 1))])]);

        var paired = bag.AddAlternatives([new Alternative([Eq(-3, (b, 1))]), new Alternative([Eq(-4, (b, 1))]), new Alternative([Eq(-5, (b, 1))])]);

        Assert.Equal(6, paired.Alternatives.Length);
        Assert.All(paired.Alternatives, alternative => Assert.Equal(2, alternative.Facts.Length));
        Assert.Equal(2, bag.Alternatives.Length);
    }

    [Fact]
    public void CapMergesTheOldestPathsIntoSingleValueIntervalSets()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var paths = Enumerable.Range(1, Bag.AlternativeCap + 1)
            .Select(index => new Alternative([Eq(-index * 10, (a, 1)), Eq(-index * 20, (b, 1))]));

        var bag = new Bag([], paths, Limit);

        Assert.Equal(Bag.AlternativeCap, bag.Alternatives.Length);
        Assert.Equal(2, bag.Alternatives[0].Facts.Length);
        Assert.All(bag.Alternatives[0].Facts, fact =>
        {
            Assert.True(fact.IsMerged);
            _ = Assert.Single(fact.Term.Coefficients);
            Assert.Equal(2, fact.Values!.Intervals.Length);
        });
        Assert.Contains(Eq(-30, (a, 1)), bag.Alternatives[1].Facts);
    }

    [Fact]
    public void AProjectionThatExhaustsItsBudgetAdmitsEverythingEveryTime()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var y = model.Value("y");
        var bag = MakeBag(Comparison.In(x, new IntegralSet([new(0, 10)])), Comparison.In(y, new IntegralSet([new(0, 5)])), Eq(0, (x, 1), (y, -1)));

        Assert.Equal(IntegralSet.Everything, Bag.Project(bag, x, new Budget(1)));
        Assert.Equal(new IntegralSet([new(0, 5)]), Bag.Project(bag, x, Limit));
        Assert.Equal(IntegralSet.Everything, Bag.Project(bag, x, new Budget(1)));
    }

    [Fact]
    public void MergePreservesHolesRatherThanTakingAnIntervalHull()
    {
        var a = new FactModel().Value("a");
        var merged = Bag.MergePerVariable([new Alternative([Eq(-1, (a, 1))]), new Alternative([Eq(-100, (a, 1))])], Limit);
        var fact = Assert.Single(merged.Facts);

        Assert.True(fact.Values!.Contains(1));
        Assert.True(fact.Values.Contains(100));
        Assert.False(fact.Values.Contains(50));
        _ = Assert.IsType<Proven>(Prove(new Bag([], [merged]), Eq(-50, (a, 1)), a));
    }

    [Fact]
    public void MergeProjectsRelationsBeforeUnioningEachValue()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var merged = Bag.MergePerVariable([new Alternative([Eq(-1, (a, 1)), Eq(0, (b, 1), (a, -2))]),
            new Alternative([Eq(-100, (a, 1)), Eq(0, (b, 1), (a, -2))])], Limit);
        var values = Assert.Single(merged.Facts, fact => fact.Subject == b).Values!;

        Assert.True(values.Contains(2));
        Assert.True(values.Contains(200));
        Assert.False(values.Contains(100));
    }

    [Fact]
    public void UnreachableBagHasAnAlternativeAndJoinsAsIdentity()
    {
        var x = new FactModel().Value("x");
        var dead = MakeBag(Eq(-1, (x, 1))).Add(Eq(-2, (x, 1)));
        var live = MakeBag(Eq(-3, (x, 1)));

        Assert.True(dead.Unreachable);
        _ = Assert.Single(dead.Alternatives);
        Assert.Same(live, dead.Join(live));
        Assert.Same(live, live.Join(dead));
        _ = Assert.IsType<Proven>(Prove(dead, Le(0, (x, 1)), x));
    }

    [Fact]
    public void ContradictionsRemoveOnlyTheirOwnAlternative()
    {
        var x = new FactModel().Value("x");
        var bag = new Bag([], [new Alternative([Eq(-1, (x, 1))]), new Alternative([Eq(-2, (x, 1))])]).Add(Eq(-2, (x, 1)));

        Assert.False(bag.Unreachable);
        _ = Assert.Single(bag.Alternatives);
    }

    [Fact]
    public void JoinKeepsCommonFactsAndPreservesBothPaths()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var common = Le(0, (a, -1));
        var joined = MakeBag(common, Eq(-2, (b, 1))).Join(MakeBag(common, Eq(-4, (b, 1))));

        Assert.Equal(common, Assert.Single(joined.Common));
        Assert.Equal(2, joined.Alternatives.Length);
    }

    [Fact]
    public void DisjointTypeIntervalsStayOneFactAndOneAlternative()
    {
        var model = new FactModel();
        var h = model.Expressions;
        var type = HandBuiltRefinements.RefinePattern(h.Int, new DisjunctionPattern(HandBuiltModel.Node(), h.Int,
            [h.Compare(h.Int, ComparisonKind.Equal, 1), h.Compare(h.Int, ComparisonKind.Equal, 100)]));
        var value = new Value(type, "a", HandBuiltModel.Node());
        var fact = Comparison.In(value, IntegralSet.Of(Requirement.OfType(type)!.Values));
        var bag = MakeBag(fact);

        _ = Assert.Single(bag.Alternatives);
        _ = Assert.Single(bag.Common);
        Assert.Equal(2, fact.Values!.Intervals.Length);
        Assert.False(fact.Values.Contains(50));
    }

    [Fact]
    public void IntegralSetUnionAndIntersectionRetainUnboundedEnds()
    {
        var low = new IntegralSet([new(null, -1)]);
        var high = new IntegralSet([new(1, null)]);
        var union = low.Or(high);

        Assert.Equal(2, union.Intervals.Length);
        Assert.False(union.Contains(0));
        Assert.True(low.And(high).IsEmpty);
        Assert.True(union.And(IntegralSet.Of(Admitted.Between(-2, 2))).Contains(2));
    }

    [Fact]
    public void MergeExhaustionSurvivesPairingAndReachesTheProofResult()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var merged = Bag.MergePerVariable([new Alternative([Eq(-1, (a, 1))]), new Alternative([Eq(-100, (a, 1))])], new Budget(0));
        var bag = MakeBag().AddAlternatives([merged]).AddAlternatives([new Alternative([Le(0, (a, -1))])]);

        Assert.True(merged.IsExhausted);
        _ = Assert.IsType<Exhausted>(Prove(bag, Le(1, (a, 1)), a));
    }

    [Fact]
    public void ImportingSignedIntervalsKeepsTheirFiniteEndpoints()
    {
        var set = IntegralSet.Of(Admitted.Exactly(long.MaxValue));

        Assert.True(set.Contains(long.MaxValue));
        Assert.False(set.Contains(new System.Numerics.BigInteger(ulong.MaxValue)));
    }
}