using FsCheck.Xunit;
using Mist.ProgramAnalysis.Facts;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class BagSharingTests
{
    [Fact]
    public void SharedFactsAfterTheCommonPrefixAreKeptInOrderWithTheRebuiltProjections()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var y = model.Value("y");
        var z = model.Value("z");
        var prefix = Bag.Empty.Add(Comparison.In(x, new IntegralSet([new(0, 10)]))).Add(Le(0, (y, 1), (x, -1)));
        _ = prefix.Intervals(x);
        _ = prefix.Intervals(y);
        var kept = Comparison.In(z, new IntegralSet([new(1, 3)]));
        var left = prefix.Add(Le(-5, (x, 1))).Add(kept).Add(Le(0, (z, 1), (y, -1)));
        var right = prefix.Add(Le(-7, (x, 1))).Add(kept);

        var shared = left.Shared(right);

        Assert.Equal([prefix.Common[0], prefix.Common[1], kept], shared.Common.ToArray());
        AssertSameAsRebuilt(left, right, shared, [x, y, z]);
    }

    [Fact]
    public void ABagWithAlternativesSharesItsFactsWithoutThem()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var y = model.Value("y");
        var start = Bag.Empty.Add(Comparison.In(x, new IntegralSet([new(0, 10)])));
        _ = start.Intervals(x);
        var left = start.AddAlternatives([new Alternative([Eq(-1, (x, 1))]), new Alternative([Eq(-2, (x, 1))])]).Add(Le(-4, (y, 1)));
        var right = start.Add(Le(-4, (y, 1)));

        var shared = left.Shared(right);

        Assert.Equal(new IntegralSet([new(1, 2)]), left.Intervals(x));
        Assert.Equal([Alternative.Empty], shared.Alternatives.ToArray());
        AssertSameAsRebuilt(left, right, shared, [x, y]);
    }

    [Property(MaxTest = 50)]
    public bool SharingAgreesWithABagRebuiltFromTheSharedFacts(int seed)
    {
        var random = new Random(seed);
        var model = new FactModel();
        var values = Enumerable.Range(0, 4).Select(index => model.Value($"v{index}")).ToArray();
        var prefix = Grow(Bag.Empty, random, values, random.Next(0, 5));
        var left = Grow(prefix, random, values, random.Next(0, 4));
        var right = Grow(prefix, random, values, random.Next(0, 4));
        if(random.Next(3) == 0)
        {
            left = left.AddAlternatives([new Alternative([Le(-3, (values[0], 1))]), new Alternative([Le(0, (values[0], -1), (values[1], 1))])]);
        }

        foreach(var fact in Enumerable.Range(0, random.Next(0, 3)).Select(_ => Draw(random, values)))
        {
            left = left.Add(fact);
            right = right.Add(fact);
            left = Grow(left, random, values, random.Next(0, 2));
        }

        if(left.Unreachable || right.Unreachable)
        {
            return true;
        }

        var shared = left.Shared(right);
        var rebuilt = new Bag(left.Common.Intersect(right.Common), [Alternative.Empty]);
        return shared.Common.SequenceEqual(rebuilt.Common) && shared.Alternatives.SequenceEqual(rebuilt.Alternatives)
            && shared.Unreachable == rebuilt.Unreachable && values.All(value => shared.Intervals(value).Equals(rebuilt.Intervals(value)));
    }

    private static Bag Grow(Bag bag, Random random, Value[] values, int count)
    {
        for(var index = 0; index < count; index++)
        {
            bag = bag.Add(Draw(random, values));
            if(random.Next(2) == 0)
            {
                _ = bag.Intervals(values[random.Next(values.Length)]);
            }
        }

        return bag;
    }

    private static Comparison Draw(Random random, Value[] values)
    {
        var first = values[random.Next(values.Length)];
        if(random.Next(2) == 0)
        {
            var low = random.Next(-4, 5);
            return Comparison.In(first, new IntegralSet([new(low, low + random.Next(0, 8))]));
        }

        var second = values[random.Next(values.Length)];
        return ReferenceEquals(first, second) ? Le(-random.Next(0, 8), (first, 1)) : Le(-random.Next(-3, 4), (first, 1), (second, -1));
    }

    private static void AssertSameAsRebuilt(Bag left, Bag right, Bag shared, Value[] values)
    {
        var rebuilt = new Bag(left.Common.Intersect(right.Common), [Alternative.Empty]);
        Assert.Equal(rebuilt.Common.ToArray(), shared.Common.ToArray());
        Assert.Equal(rebuilt.Alternatives.ToArray(), shared.Alternatives.ToArray());
        Assert.Equal(rebuilt.Unreachable, shared.Unreachable);
        Assert.All(values, value => Assert.Equal(rebuilt.Intervals(value), shared.Intervals(value)));
    }
}