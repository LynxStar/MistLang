using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class FailingCaseTests
{
    [Fact]
    public void ReserveWithoutANonnegativeCapacityFailsWhenCapacityIsNegative()
    {
        var model = new FactModel();
        var h = model.Expressions;
        var used = model.Value("used");
        var capacity = new Value(h.Between(h.Int, -1_000_000L, 1_000_000L, "Signed"), "capacity", HandBuiltModel.Node());
        var bag = MakeBag(Le(-1_000_000, (capacity, 1)), Le(-1_000_000, (capacity, -1)), Le(0, (used, 1), (capacity, -1)));

        var refused = Assert.IsType<Refuted>(Prove(bag, Le(1, (capacity, 2), (used, -1)), used, capacity));

        Assert.Equal("fails when capacity <= -1", FailingCase.Describe(refused));
    }

    [Fact]
    public void ARelationNothingNarrowsIsStatedOverTheContractValues()
    {
        var model = new FactModel();
        var used = model.Value("used");
        var product = model.Value("capacity * 2");
        var bag = MakeBag(Le(-int.MaxValue, (used, 1)), Le(int.MinValue, (used, -1)), Le(-int.MaxValue, (product, 1)), Le(int.MinValue, (product, -1)));

        var refused = Assert.IsType<Refuted>(Prove(bag, Le(1, (product, 1), (used, -1)), used, product));

        Assert.Equal("fails when used >= capacity * 2 + 1", FailingCase.Describe(refused));
    }

    [Fact]
    public void OppositeBoundsReadAsAnEqualityAndTheDeclaredRangeIsLeftOut()
    {
        var model = new FactModel();
        var h = model.Expressions;
        var start = new Value(h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, "ZeroOrPositiveInt"), "start", HandBuiltModel.Node());
        var length = model.Value("items.Length");
        var failing = new Alternative([Le(0, (start, -1)), Le(0, (length, -1)), Le(0, (start, 1), (length, -1)), Le(0, (start, -1), (length, 1))]);

        Assert.Equal("fails when start == items.Length and items.Length >= 0", FailingCase.Describe(new Refuted(failing, false)));
    }

    [Fact]
    public void AFailingCaseRestingOnAMergedFactSaysPathsWereMerged()
    {
        var x = new FactModel().Value("x");

        Assert.Equal("fails when x >= 11, after merging paths", FailingCase.Describe(new Refuted(new Alternative([Le(11, (x, -1))]), true)));
    }

    [Fact]
    public void AFailingCaseOfTypeRangesAloneHasNothingToSay()
    {
        var x = new FactModel().Value("x");

        Assert.Null(FailingCase.Describe(new Refuted(new Alternative([Le(-int.MaxValue, (x, 1)), Le(int.MinValue, (x, -1))]), false)));
    }
}