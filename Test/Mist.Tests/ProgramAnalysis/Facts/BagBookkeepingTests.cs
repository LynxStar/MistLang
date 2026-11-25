using Mist.ProgramAnalysis.Facts;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class BagBookkeepingTests
{
    [Fact]
    public void FactsAboutOneValueGivenTogetherMeetRatherThanReplaceEachOther()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var one = Eq(-1, (x, 1));
        var two = Eq(-2, (x, 1));
        var three = Eq(-3, (x, 1));

        Assert.Equal(new IntegralSet([new(0, 5)]), MakeBag(Comparison.In(x, new IntegralSet([new(0, 10)])), Le(-5, (x, 1))).Intervals(x));
        Assert.True(MakeBag(one, two).Unreachable);

        var paths = new Bag([], [new Alternative([one, two]), new Alternative([three])]);

        Assert.Equal([new Alternative([three])], paths.Alternatives.ToArray());
        Assert.Equal(new IntegralSet([new(3, 3)]), paths.Intervals(x));
    }

    [Fact]
    public void AConstantFactAddedOnBothPathsIsHeldAndStaysCommonWhenThePathsJoin()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var prefix = MakeBag(Comparison.In(x, new IntegralSet([new(0, 10)])));
        var constant = new Comparison(new Term(-1));
        var left = prefix.Add(constant);
        var right = prefix.Add(constant);

        Assert.Same(left, left.Add(constant));

        var joined = left.Join(right);

        Assert.Equal([prefix.Common[0], constant], joined.Common.ToArray());
        Assert.Equal([Alternative.Empty], joined.Alternatives.ToArray());
    }
}