using System.Collections.Immutable;
using Mist.ProgramAnalysis.Facts;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "2")]
public sealed class ProjectionBudgetCacheTests
{
    [Fact]
    public void CachedProjectionReturnsTheDerivedIntervalAndChargesTheSharedBudget()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var y = model.Value("y");
        ImmutableArray<Comparison> facts = [Eq(-2, (x, 1), (y, -1)), Le(3, (y, -1)), Le(-5, (y, 1))];
        var expected = new IntegralSet([new(5, 7)]);
        var cold = new ProofBudget(new Budget(3));
        Assert.True(cold.Derive());

        Assert.Equal(expected, Chain.ProjectedIntervals(facts, x, cold));

        var warm = new ProofBudget(new Budget(3));
        Assert.Equal(expected, Chain.ProjectedIntervals(facts, x, warm));
        Assert.Equal(2, warm.Derived);
        Assert.Null(Chain.ProjectedIntervals(facts, x, warm));
    }
}