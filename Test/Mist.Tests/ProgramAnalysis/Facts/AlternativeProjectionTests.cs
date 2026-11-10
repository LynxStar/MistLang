using Mist.ProgramAnalysis.Facts;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class AlternativeProjectionTests
{
    [Fact]
    public void ProjectionFollowsEveryLinkInFactOrderAndKeepsConstants()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var y = model.Value("y");
        var z = model.Value("z");
        var other = model.Value("other");
        var yz = Eq(0, (y, 1), (z, -1));
        var unrelated = Eq(-8, (other, 1));
        var xy = Le(0, (x, 1), (y, -1));
        var bound = Le(-3, (z, 1));
        var constant = new Comparison(new Term(-1));
        var alternative = new Alternative([yz, unrelated, xy, bound, constant]);

        Assert.Equal<Comparison>([yz, xy, bound, constant], alternative.Relevant([x]));
        Assert.Equal<Comparison>([unrelated, constant], alternative.Relevant([other]));

        var linked = alternative.Add([Eq(0, (z, 1), (other, -1))]);

        Assert.Equal<Comparison>(linked.Facts, linked.Relevant([x]));
        Assert.Equal<Comparison>([yz, xy, bound, constant], alternative.Relevant([x]));
    }

    [Fact]
    public void BoundsKeepHolesAndExcludeContradictionsWithoutInferringRelations()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var y = model.Value("y");
        var hole = new IntegralSet([new(1, 1), new(100, 100)]);
        var alternative = new Alternative([Comparison.In(x, hole), Eq(0, (x, 1), (y, -1))]);

        Assert.Equal(hole, alternative.Bound(x));
        Assert.Equal(IntegralSet.Everything, alternative.Bound(y));
        Assert.Equal(new IntegralSet([new(1, 1)]), alternative.Add([Le(-50, (x, 1))]).Bound(x));
        Assert.Equal(IntegralSet.Nothing, alternative.Add([new Comparison(new Term(1))]).Bound(y));
        Assert.Equal(hole, alternative.Bound(x));
    }

    [Fact]
    public void EqualityKeepsEachAlternativesOrderAndDistinguishesExhaustion()
    {
        var model = new FactModel();
        var x = Eq(-1, (model.Value("x"), 1));
        var y = Eq(-2, (model.Value("y"), 1));
        var first = new Alternative([x, y, x]);
        var second = new Alternative([y, x]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal<Comparison>([x, y], first.Facts);
        Assert.Equal<Comparison>([y, x], second.Facts);
        Assert.NotEqual(first, first.Add([], exhausted: true));
        Assert.Equal(first, first.Add([y, x]));
    }
}