using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
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
    public void AnIndexGuardedByAnotherValueFailsWhenItReachesTheLength()
    {
        var model = new FactModel();
        var natural = Natural(model);
        var n = new Value(natural, "n", HandBuiltModel.Node());
        var m = new Value(natural, "m", HandBuiltModel.Node());
        var i = new Value(natural, "i", HandBuiltModel.Node());
        var bag = MakeBag([.. Within(n, 0), .. Within(m, 0), .. Within(i, 0), Le(1, (i, 1), (m, -1))]);

        var refused = Assert.IsType<Refuted>(Prove(bag, Le(0, (n, 1), (i, -1)), i, n));

        Assert.Equal("fails when n <= i", FailingCase.Describe(refused));
    }

    [Fact]
    public void ALengthKnownOnlyToBeNonnegativeStaysInTheCase()
    {
        var model = new FactModel();
        var n = new Value(Natural(model), "n", HandBuiltModel.Node());
        var first = model.Value("Zeroes(n).Length");
        var second = model.Value("b.Length");
        var bag = MakeBag([.. Within(n, 0), Le(0, (first, -1)), Le(0, (second, -1))]);

        var refused = Assert.IsType<Refuted>(Chain.Prove(bag, [new Alternative([Le(1, (first, 1), (n, -1))]), new Alternative([Le(1, (second, 1), (n, -1))])],
            new HashSet<Value> { n, first, second }, Limit));

        Assert.Equal("fails when n >= Zeroes(n).Length + 1 and Zeroes(n).Length >= 0", FailingCase.Describe(refused));
    }

    [Fact]
    public void AKnownLengthIsStatedWithTheBoundItBroke()
    {
        var model = new FactModel();
        var n = new Value(Natural(model), "n", HandBuiltModel.Node());
        var first = model.Value("new int[5].Length");
        var second = model.Value("new int[3].Length");
        var bag = MakeBag([.. Within(n, 0), Fact.In(first, new IntegralSet([new(5, 5)])), Fact.In(second, new IntegralSet([new(3, 3)]))]);

        var refused = Assert.IsType<Refuted>(Chain.Prove(bag, [new Alternative([Le(1, (first, 1), (n, -1))]), new Alternative([Le(1, (second, 1), (n, -1))])],
            new HashSet<Value> { n, first, second }, Limit));

        Assert.Equal("fails when new int[5].Length == 5 and n >= 6", FailingCase.Describe(refused));
    }

    [Fact]
    public void AFactAboutAValueTheBrokenBoundDoesNotReachIsLeftOut()
    {
        var model = new FactModel();
        var index = model.Value("k++");
        var length = model.Value("items.Length");
        var bag = MakeBag([.. Within(index), Fact.In(length, new IntegralSet([new(3, 3)]))]);

        var refused = Assert.IsType<Refuted>(Chain.Prove(bag, [new Alternative([Le(1, (index, 1))]), new Alternative([Le(0, (length, 1), (index, -1))])],
            new HashSet<Value> { index, length }, Limit));

        Assert.Equal("fails when k++ <= -1", FailingCase.Describe(refused));
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

    private static RefinedType Natural(FactModel model)
    {
        return model.Expressions.Refine(model.Expressions.Int, ComparisonKind.GreaterThanOrEqual, 0L, "ZeroOrPositiveInt");
    }

    private static Fact[] Within(Value value, long low = int.MinValue)
    {
        return [Le(low, (value, -1)), Le(-int.MaxValue, (value, 1))];
    }
}