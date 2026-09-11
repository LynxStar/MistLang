// Tier 1 (Unit): the interval set every refinement proof stands on. Pure arithmetic over closed
// intervals of long, so each fact is checked on the values themselves.

using Mist.ProgramAnalysis.Refinements;

namespace Mist.Tests.ProgramAnalysis;

public sealed class AdmittedTests
{
    [Theory]
    [InlineData(18, 18, true)]
    [InlineData(18, 17, false)]
    [InlineData(18, long.MaxValue, true)]
    public void AtLeastAdmitsTheValueAndEverythingAbove(long bound, long value, bool expected)
    {
        Assert.Equal(expected, Admitted.AtLeast(bound).Contains(value));
    }

    [Fact]
    public void AboveAndBelowExcludeTheBoundItself()
    {
        Assert.False(Admitted.Above(5).Contains(5));
        Assert.True(Admitted.Above(5).Contains(6));
        Assert.False(Admitted.Below(5).Contains(5));
        Assert.True(Admitted.Below(5).Contains(4));
    }

    [Fact]
    public void AConjunctionIsTheIntersection()
    {
        var range = Admitted.AtLeast(0).And(Admitted.AtMost(100));

        Assert.True(range.Contains(0));
        Assert.True(range.Contains(100));
        Assert.False(range.Contains(101));
        Assert.Equal(Admitted.Between(0, 100), range);
        Assert.Equal(Admitted.Nothing, Admitted.AtLeast(10).And(Admitted.AtMost(5)));
    }

    [Fact]
    public void ADisjunctionMergesAdjacentAndOverlappingIntervals()
    {
        Assert.Equal(Admitted.Between(7, 8), Admitted.Exactly(7).Or(Admitted.Exactly(8)));
        Assert.Equal(Admitted.Everything, Admitted.AtLeast(5).Or(Admitted.Below(5)));
        Assert.Equal(2, Admitted.Exactly(7).Or(Admitted.Exactly(9)).Intervals.Length);
    }

    [Fact]
    public void NegationIsTheComplement()
    {
        Assert.Equal(Admitted.Below(0), Admitted.AtLeast(0).Not());
        Assert.Equal(Admitted.Below(7).Or(Admitted.Above(7)), Admitted.Exactly(7).Not());
        Assert.Equal(Admitted.Everything, Admitted.Nothing.Not());
        Assert.Equal(Admitted.Nothing, Admitted.Everything.Not());
        Assert.Equal(Admitted.Between(3, 8), Admitted.Between(3, 8).Not().Not());
    }

    [Theory]
    [InlineData(21, 18, true)]
    [InlineData(18, 21, false)]
    public void ALowerBoundImpliesAWeakerOne(long stronger, long weaker, bool expected)
    {
        Assert.Equal(expected, Admitted.AtLeast(stronger).IsSubsetOf(Admitted.AtLeast(weaker)));
    }

    [Fact]
    public void ImplicationIsSubsetAcrossOperators()
    {
        Assert.True(Admitted.Exactly(7).IsSubsetOf(Admitted.AtLeast(0).And(Admitted.AtMost(10))));
        Assert.True(Admitted.Between(1, 49).IsSubsetOf(Admitted.Between(0, 100)));
        Assert.True(Admitted.Between(7, 8).IsSubsetOf(Admitted.AtLeast(7)));
        Assert.False(Admitted.Between(7, 9).IsSubsetOf(Admitted.AtMost(8)));
        Assert.True(Admitted.AtLeast(10).IsSubsetOf(Admitted.Above(5)));
        Assert.False(Admitted.Exactly(7).Not().IsSubsetOf(Admitted.Exactly(5).Not()));
        Assert.True(Admitted.Nothing.IsSubsetOf(Admitted.Exactly(1)));
    }

    [Fact]
    public void ShiftingMovesEveryIntervalAndSaturatesAtInfinity()
    {
        Assert.Equal(Admitted.Between(1, 5), Admitted.Between(0, 4).Shifted(1));
        Assert.Equal(Admitted.AtLeast(1), Admitted.AtLeast(0).Shifted(1));
        Assert.Equal(Admitted.AtMost(-1), Admitted.AtMost(0).Shifted(-1));
    }

    [Fact]
    public void WideningOpensTheDirectionThatGrew()
    {
        Assert.Equal(Admitted.AtLeast(0), Admitted.Between(0, 5).WidenedAgainst(Admitted.Between(0, 4)));
        Assert.Equal(Admitted.AtMost(5), Admitted.Between(-1, 5).WidenedAgainst(Admitted.Between(0, 5)));
        Assert.Equal(Admitted.Between(0, 5), Admitted.Between(0, 5).WidenedAgainst(Admitted.Between(0, 5)));
    }

    [Fact]
    public void DescribesItselfAsAPredicate()
    {
        Assert.Equal(">= 18", Admitted.AtLeast(18).ToString());
        Assert.Equal(">= 0 and <= 100", Admitted.Between(0, 100).ToString());
        Assert.Equal("== 7", Admitted.Exactly(7).ToString());
        Assert.Equal("<= 6 or >= 8", Admitted.Exactly(7).Not().ToString());
    }
}