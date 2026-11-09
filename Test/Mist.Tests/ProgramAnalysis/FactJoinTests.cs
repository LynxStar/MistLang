using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.Tests.ProgramAnalysis.Facts;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FactJoinTests
{
    [Fact]
    public void JoiningRebindsBothHoldersInsideTheirPairedAlternatives()
    {
        var model = new FactModel();
        var a = new object();
        var b = new object();
        var a1 = model.Value("a");
        var b1 = model.Value("b");
        var a2 = model.Value("a");
        var b2 = model.Value("b");
        var left = State((a, a1, 1, 1), (b, b1, 2, 2));
        var right = State((a, a2, 100, 100), (b, b2, 200, 200));

        var joined = Lattice().Join(left, right);
        var ma = joined.Bindings.Get(a)!;
        var mb = joined.Bindings.Get(b)!;

        Assert.Equal(2, joined.Bag.Alternatives.Length);
        _ = Assert.IsType<Proven>(Prove(joined.Bag, Le(0, (mb, 1), (ma, -1)), ma, mb));
        Assert.Contains(Eq(0, (ma, 1), (a1, -1)), joined.Bag.Alternatives[0].Facts);
        Assert.Contains(Eq(0, (mb, 1), (b2, -1)), joined.Bag.Alternatives[1].Facts);
        Assert.DoesNotContain(joined.Bag.Common, fact => fact.IsMerged);
    }

    [Fact]
    public void SameDistinguishesPairingsEvenWhenEveryVariableHasTheSameProjection()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var bindings = Bindings.Empty.Set("a", a).Set("b", b);
        var left = FactState.Empty with { Bindings = bindings, Bag = new Bag([], [new Alternative([Eq(-1, (a, 1)), Eq(-2, (b, 1))]), new Alternative([Eq(-100, (a, 1)), Eq(-200, (b, 1))])]) };
        var right = left with { Bag = new Bag([], [new Alternative([Eq(-1, (a, 1)), Eq(-200, (b, 1))]), new Alternative([Eq(-100, (a, 1)), Eq(-2, (b, 1))])]) };

        Assert.False(Lattice().Same(left, right));
        Assert.True(Lattice().Same(left, left with { Bag = new Bag(left.Bag.Common, left.Bag.Alternatives) }));
    }

    [Theory]
    [InlineData(3, 0, 5)]
    [InlineData(4, 0, int.MaxValue)]
    public void WidenWaitsThreeRoundsAndKeepsTheUnmovedLowerEnd(int round, int low, int high)
    {
        var model = new FactModel();
        var holder = new object();
        var before = State((holder, model.Value("i"), 0, 3));
        var joined = State((holder, model.Value("i"), 0, 5));

        var widened = Lattice().Widen(before, joined, round);

        Assert.Equal(new IntegralSet([new(low, high)]), widened.Intervals(widened.Bindings.Get(holder)!));
        if(round == 3)
        {
            Assert.Same(joined, widened);
        }
    }

    [Fact]
    public void WidenDropsTheMovedLowerEndAndKeepsUnchangedValuesAndRelations()
    {
        var model = new FactModel();
        var i = new object();
        var n = model.Value("n");
        var length = model.Value("Length");
        var relation = Le(0, (n, 1), (length, -1));
        var previous = State((i, model.Value("i"), -3, 10));
        previous = previous with { Bindings = previous.Bindings.Set("n", n).Set("Length", length), Bag = previous.Bag.Add(relation) };
        var joined = State((i, model.Value("i"), -5, 10));
        var moved = joined.Bindings.Get(i)!;
        joined = joined with { Bindings = joined.Bindings.Set("n", n).Set("Length", length), Bag = joined.Bag.Add(relation).Add(Le(0, (moved, 1), (n, -1))) };

        var widened = Lattice().Widen(previous, joined, 4);

        Assert.Equal(new IntegralSet([new(int.MinValue, 10)]), widened.Intervals(widened.Bindings.Get(i)!));
        Assert.Same(n, widened.Bindings.Get("n"));
        Assert.Contains(relation, widened.Bag.Common);
        Assert.DoesNotContain(widened.Bag.Common, fact => fact.Term.Variables.Length > 1 && fact.Term.Variables.Contains(moved));
    }

    [Fact]
    public void WidenKeepsTheFactsAnUnchangedCopyReachesThroughAnOldValue()
    {
        var model = new FactModel();
        var holder = new object();
        var before = model.Value("i");
        var after = model.Value("i");
        var copy = model.Value("copy");
        var oldBounds = Comparison.In(before, new IntegralSet([new(0, 3)]));
        var copied = Eq(0, (copy, 1), (before, -1));
        var previous = State((holder, before, 0, 3));
        previous = previous with { Bindings = previous.Bindings.Set("copy", copy), Bag = previous.Bag.Add(copied) };
        var joined = State((holder, after, 0, 5));
        joined = joined with { Bindings = joined.Bindings.Set("copy", copy), Bag = joined.Bag.Add(oldBounds).Add(copied) };

        var widened = Lattice().Widen(previous, joined, 4);

        Assert.Equal(new IntegralSet([new(0, 3)]), widened.Intervals(copy));
        Assert.Equal(new IntegralSet([new(0, int.MaxValue)]), widened.Intervals(after));
    }

    private static FactFlow Lattice()
    {
        return new FactFlow(new HashSet<object>(), new LiftedReturns());
    }

    private static FactState State(params (object Holder, Value Value, int Low, int High)[] bindings)
    {
        var state = FactState.Empty;
        foreach(var (holder, value, low, high) in bindings)
        {
            state = state with { Bindings = state.Bindings.Set(holder, value), Bag = state.Bag.Add(Comparison.In(value, new IntegralSet([new(low, high)]))) };
        }

        return state;
    }
}