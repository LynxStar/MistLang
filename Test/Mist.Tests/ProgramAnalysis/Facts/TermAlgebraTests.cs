using System.Globalization;
using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class TermAlgebraTests
{
    [Fact]
    public void ConstructionSumsRepeatedValuesDropsCancelledOnesAndOrdersByCreation()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var c = model.Value("c");

        var term = Term(9, (c, 5), (a, 3), (b, 1), (a, 4), (b, -1));
        var permuted = Term(9, (b, -1), (a, 4), (c, 5), (b, 1), (a, 3));

        Assert.Equal<Value>([a, c], term.Variables);
        Assert.Equal<BigInteger>([7, 5], term.Multipliers);
        Assert.Equal(9, term.Constant);
        Assert.Equal<Value>([a, c], permuted.Variables);
        Assert.Equal(term, permuted);
        Assert.Equal(term.GetHashCode(), permuted.GetHashCode());

        var cancelled = Term(3, (a, 2), (b, 1), (a, -2), (b, -1));
        Assert.Empty(cancelled.Variables);
        Assert.Equal(new Term(3), cancelled);
    }

    [Fact]
    public void AddMergesInterleavedAndDisjointVariablesInCreationOrder()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var c = model.Value("c");
        var d = model.Value("d");

        var interleaved = Term(2, (a, 1), (c, 2)).Add(Term(-5, (b, 1), (c, -1), (d, 3)));
        Assert.Equal<Value>([a, b, c, d], interleaved.Variables);
        Assert.Equal<BigInteger>([1, 1, 1, 3], interleaved.Multipliers);
        Assert.Equal(-3, interleaved.Constant);

        var laterFirst = Term(0, (c, 4), (d, -2)).Add(Term(0, (a, 6), (b, -7)));
        Assert.Equal<Value>([a, b, c, d], laterFirst.Variables);
        Assert.Equal<BigInteger>([6, -7, 4, -2], laterFirst.Multipliers);
        Assert.Equal(laterFirst, Term(0, (a, 6), (b, -7)).Add(Term(0, (c, 4), (d, -2))));
    }

    [Fact]
    public void AddDropsCancelledVariablesAndSumsConstants()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var c = model.Value("c");

        var partial = Term(1, (a, 2), (b, 3), (c, -1)).Add(Term(4, (b, -3)));
        Assert.Equal<Value>([a, c], partial.Variables);
        Assert.Equal<BigInteger>([2, -1], partial.Multipliers);
        Assert.Equal(5, partial.Constant);

        var whole = Term(5, (a, 1), (b, -2)).Add(Term(-5, (a, -1), (b, 2)));
        Assert.Empty(whole.Variables);
        Assert.Equal(new Term(0), whole);
        Assert.Equal(new Term(0).GetHashCode(), whole.GetHashCode());

        Assert.Equal(new Term(-7), new Term(3).Add(new Term(-10)));
    }

    [Fact]
    public void ScaleMultipliesEveryCoefficientAndTheConstant()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var term = Term(4, (a, 2), (b, -3));

        var negated = term.Scale(-3);
        Assert.Equal<Value>([a, b], negated.Variables);
        Assert.Equal<BigInteger>([-6, 9], negated.Multipliers);
        Assert.Equal(-12, negated.Constant);

        var zero = term.Scale(0);
        Assert.Empty(zero.Variables);
        Assert.Equal(new Term(0), zero);
    }

    [Fact]
    public void SubtractIsAddingTheNegation()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var term = Term(4, (a, 2), (b, -3));

        Assert.Equal(new Term(0), term.Subtract(term));
        Assert.Equal(Term(3, (a, 1), (b, -1)), Term(5, (a, 1)).Subtract(Term(2, (b, 1))));
    }

    [Fact]
    public void MultiplierOfAnAbsentOrCancelledValueIsZero()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var c = model.Value("c");
        var term = Term(1, (a, -4), (c, 1)).Add(Term(0, (c, -1)));

        Assert.Equal(-4, term.Multiplier(a));
        Assert.Equal(0, term.Multiplier(b));
        Assert.Equal(0, term.Multiplier(c));
    }

    [Fact]
    public void EqualityIsStructuralWhicheverWayATermIsBuilt()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var constructed = Term(4, (b, 2), (a, 1));
        var built = new Term(a).Add(new Term(b).Scale(2)).Add(new Term(4));

        Assert.Equal(constructed, built);
        Assert.Equal(constructed.GetHashCode(), built.GetHashCode());
        Assert.True(constructed.Equals((object)built));
        Assert.NotEqual(constructed, Term(5, (a, 1), (b, 2)));
        Assert.NotEqual(constructed, Term(4, (a, 1), (b, 3)));
        Assert.NotEqual(constructed, Term(4, (a, 2), (b, 1)));
        Assert.NotEqual(new Term(a), new Term(b));
        Assert.False(constructed.Equals((object?)null));
        Assert.False(constructed.Equals("a + 2 * b + 4"));
    }

    [Fact]
    public void DisplayWritesUnitCoefficientsBareAndOmitsAZeroConstantBesideVariables()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var y = model.Value("y");

        Assert.Equal("x", new Term(x).ToString());
        Assert.Equal("x + 2 * y + 3", Term(3, (y, 2), (x, 1)).ToString());
        Assert.Equal("-1 * x + y", Term(0, (x, -1), (y, 1)).ToString());
        Assert.Equal("0", new Term(0).ToString());
        Assert.Equal("0", Term(0, (x, 1)).Subtract(new Term(x)).ToString());
    }

    [Fact]
    public void DisplayIgnoresTheCurrentCulture()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;

        try
        {
            Assert.Equal("~3", (-3).ToString(CultureInfo.CurrentCulture));
            Assert.Equal("-3 * x + -4", Term(-4, (x, -3)).ToString());
            Assert.Equal("-5", new Term(-5).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}