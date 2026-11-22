using Mist.ProgramAnalysis.Refinements;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class AdmittedIntervalEdgeTests
{
    [Fact]
    public void MinusOfAnEmptyOperand_IsNothing()
    {
        Assert.Equal(Admitted.Nothing, Admitted.Nothing.Minus(Admitted.Between(1, 3)));
        Assert.Equal(Admitted.Nothing, Admitted.Between(1, 3).Minus(Admitted.Nothing));
    }

    [Fact]
    public void MinusSpansTheLowestLowLessTheHighestHighToTheHighestHighLessTheLowestLow()
    {
        var left = Admitted.Between(10, 12).Or(Admitted.Between(18, 20));
        var right = Admitted.Exactly(1).Or(Admitted.Exactly(3));

        Assert.Equal(Admitted.Between(7, 19), left.Minus(right));
    }

    [Fact]
    public void MinusLeavesAnEndUnboundedWhenAnOperandEndIsInfinite()
    {
        Assert.Equal(Admitted.AtLeast(2), Admitted.AtLeast(5).Minus(Admitted.Between(1, 3)));
        Assert.Equal(Admitted.AtMost(4), Admitted.AtMost(5).Minus(Admitted.Between(1, 3)));
        Assert.Equal(Admitted.AtMost(3), Admitted.Between(1, 3).Minus(Admitted.AtLeast(0)));
        Assert.Equal(Admitted.AtLeast(-5), Admitted.Exactly(0).Minus(Admitted.AtMost(5)));
    }

    [Fact]
    public void MinusLeavesAnEndUnboundedWhenItsDifferenceOverflows()
    {
        Assert.Equal(Admitted.AtMost(-2), Admitted.Between(long.MinValue + 1, 0).Minus(Admitted.Exactly(2)));
        Assert.Equal(Admitted.AtLeast(2), Admitted.Between(0, long.MaxValue - 1).Minus(Admitted.Exactly(-2)));
    }

    [Fact]
    public void ShiftingKeepsInfiniteEndsInfiniteAgainstTheShift()
    {
        Assert.Equal(Admitted.AtLeast(-1), Admitted.AtLeast(0).Shifted(-1));
        Assert.Equal(Admitted.AtMost(1), Admitted.AtMost(0).Shifted(1));
        Assert.Equal(Admitted.Everything, Admitted.Everything.Shifted(5));
    }

    [Fact]
    public void ShiftingSaturatesAFiniteEndThatOverflows()
    {
        Assert.Equal(Admitted.AtLeast(15), Admitted.Between(10, long.MaxValue - 1).Shifted(5));
        Assert.Equal(Admitted.AtMost(-15), Admitted.Between(long.MinValue + 1, -10).Shifted(-5));
    }

    [Fact]
    public void ShiftingMergesIntervalsThatSaturateTogether()
    {
        var spread = Admitted.Between(long.MaxValue - 10, long.MaxValue - 8).Or(Admitted.Between(long.MaxValue - 4, long.MaxValue - 2));

        Assert.Equal(Admitted.AtLeast(long.MaxValue - 1), spread.Shifted(9));
    }

    [Fact]
    public void ExclusiveBoundsAtTheExtremes_AreNothing()
    {
        Assert.Equal(Admitted.Nothing, Admitted.Above(long.MaxValue));
        Assert.Equal(Admitted.Nothing, Admitted.Below(long.MinValue));
        Assert.Equal(Admitted.Everything, Admitted.Above(long.MinValue).Or(Admitted.Exactly(long.MinValue)));
        Assert.Equal(Admitted.Everything, Admitted.Below(long.MaxValue).Or(Admitted.Exactly(long.MaxValue)));
    }

    [Fact]
    public void AnInvertedRangeIsNothingAndADegenerateOneIsItsValue()
    {
        Assert.Equal(Admitted.Nothing, Admitted.Between(5, 4));
        Assert.Equal(Admitted.Exactly(4), Admitted.Between(4, 4));
    }

    [Fact]
    public void ADisjunctionAbsorbsEverythingAfterAnIntervalReachingTheTop()
    {
        var union = Admitted.AtLeast(0).Or(Admitted.Between(10, 20));

        Assert.Equal(new Interval(0, long.MaxValue), Assert.Single(union.Intervals));
    }

    [Fact]
    public void ADisjunctionKeepsTheContainingIntervalAndSortsItsOperands()
    {
        Assert.Equal(Admitted.Between(0, 10), Admitted.Between(0, 10).Or(Admitted.Between(2, 3)));
        Assert.Equal<Interval>([new Interval(7, 7), new Interval(9, 9)], Admitted.Exactly(9).Or(Admitted.Exactly(7)).Intervals);
    }

    [Fact]
    public void AConjunctionOfMultiIntervalSetsIntersectsEveryPair()
    {
        var left = Admitted.Between(0, 10).Or(Admitted.Between(20, 30));
        var right = Admitted.Between(5, 25).Or(Admitted.Between(28, 40));

        Assert.Equal<Interval>([new Interval(5, 10), new Interval(20, 25), new Interval(28, 30)], left.And(right).Intervals);
    }

    [Fact]
    public void OnlyTheWholeLineIsEverything()
    {
        Assert.True(Admitted.Everything.IsEverything);
        Assert.True(Admitted.Exactly(0).Not().Or(Admitted.Exactly(0)).IsEverything);
        Assert.False(Admitted.AtLeast(long.MinValue + 1).IsEverything);
        Assert.False(Admitted.AtMost(long.MaxValue - 1).IsEverything);
        Assert.False(Admitted.Exactly(0).Not().IsEverything);
        Assert.False(Admitted.Nothing.IsEverything);
    }

    [Fact]
    public void TheEndsComeFromTheOutermostIntervals()
    {
        var spread = Admitted.Exactly(9).Or(Admitted.Between(-3, 0));

        Assert.Equal(-3, spread.Low);
        Assert.Equal(9, spread.High);
        Assert.Null(Admitted.Nothing.Low);
        Assert.Null(Admitted.Nothing.High);
    }

    [Fact]
    public void SetsEqualAfterNormalisation_AreEqualAndHashAlike()
    {
        var merged = Admitted.Exactly(7).Or(Admitted.Exactly(8));
        var direct = Admitted.Between(7, 8);

        Assert.True(merged.Equals((object)direct));
        Assert.Equal(direct.GetHashCode(), merged.GetHashCode());
        Assert.Equal(Admitted.Below(7).Or(Admitted.Above(7)).GetHashCode(), Admitted.Exactly(7).Not().GetHashCode());
        Assert.False(direct.Equals(null));
        Assert.False(direct.Equals("== 7"));
        Assert.NotEqual(Admitted.Between(7, 9), direct);
    }
}