using Mist.ProgramAnalysis.Facts;
using Mist.Tests.Helpers;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class BagCoveringJoinTests
{
    [Fact]
    public void ComplementaryComparisonsDoNotAddAlternativesToUnchangedValues()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var prefix = MakeBag(Le(0, (a, -1)), Le(-10, (b, 1)));
        var left = prefix.Add(Le(0, (a, 1), (b, -1)));
        var right = prefix.Add(Le(1, (b, 1), (a, -1)));

        var joined = left.Join(right);

        Assert.Equal(prefix.Common.ToArray(), joined.Common.ToArray());
        Assert.Empty(Assert.Single(joined.Alternatives).Facts);
    }

    [Fact]
    public void BothBooleanValuesKeepExistingPairingsWithoutSpendingTheCap()
    {
        var model = new FactModel();
        var flag = model.Value("flag");
        var a = model.Value("a");
        var b = model.Value("b");
        var prefix = new Bag([Comparison.In(flag, new IntegralSet([new(0, 1)]))],
            [new Alternative([Eq(-1, (a, 1)), Eq(-2, (b, 1))]), new Alternative([Eq(-100, (a, 1)), Eq(-200, (b, 1))])], alternativeCap: 2);

        var joined = prefix.Add(Eq(-1, (flag, 1))).Join(prefix.Add(Eq(0, (flag, 1))));

        Assert.Equal(prefix.Alternatives.ToArray(), joined.Alternatives.ToArray());
        _ = Assert.IsType<Proven>(Prove(joined, Le(0, (b, 1), (a, -1)), a, b));
        Assert.DoesNotContain(joined.Alternatives.SelectMany(alternative => alternative.Facts), fact => fact.IsMerged);
    }

    [Fact]
    public void RealComparisonsKeepTheOrderedWitnessTheirUnionCarries()
    {
        var built = new HandBuiltModel();
        var real = built.Type("Double");
        var a = new Value(real, "a", HandBuiltModel.Node());
        var b = new Value(real, "b", HandBuiltModel.Node());
        var left = Bag.Empty.Add(new Comparison(new Term(a).Subtract(new Term(b))));
        var right = Bag.Empty.Add(new Comparison(new Term(b).Subtract(new Term(a)), Relation.LessThan));

        var joined = left.Join(right);

        Assert.Equal(2, joined.Alternatives.Length);
        Assert.All(joined.Alternatives, alternative => Assert.NotEmpty(alternative.Facts));
    }

    [Fact]
    public void EqualitiesThatLeaveAHoleKeepTheirAlternatives()
    {
        var model = new FactModel();
        var value = model.Value("value");
        var prefix = MakeBag(Comparison.In(value, new IntegralSet([new(0, 2)])));

        var joined = prefix.Add(Eq(0, (value, 1))).Join(prefix.Add(Eq(-2, (value, 1))));

        Assert.Equal(2, joined.Alternatives.Length);
        Assert.Equal(new IntegralSet([new(0, 0), new(2, 2)]), joined.Intervals(value));
    }
}