// Tier 1: runtime real comparisons and set operations must agree at representable and nonfinite boundaries.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RealSetTests
{
    [Fact]
    public void SingleComparisonsUseTheActualPromotedBound()
    {
        var values = Singles();
        var bounds = values.Select(value => Value(value)).Concat(new[]
        {
            Value(0.1), Value(double.BitDecrement((double)0.1f)), Value(double.BitIncrement((double)0.1f)),
            Value((double)float.MaxValue), Value(double.BitIncrement(float.MaxValue)),
            Value(double.BitDecrement((double)-float.MaxValue)), Value(double.Epsilon), Value(-double.Epsilon),
            Value(16777217), Value(uint.MaxValue), Value(long.MinValue), Value(ulong.MaxValue),
        });

        foreach(var kind in Enum.GetValues<ComparisonKind>())
        {
            foreach(var bound in bounds)
            {
                var set = RealSet.Compare(RealKind.Single, kind, bound)!;
                var promoted = bound.Value is double number ? number : (double)Convert.ToSingle(bound.Value);

                foreach(var value in values)
                {
                    Assert.Equal(Compare(value, kind, promoted), set.Contains(Value(value)));
                }
            }
        }

        Assert.Equal(RealSet.Nothing(RealKind.Single), RealSet.Compare(RealKind.Single, ComparisonKind.Equal, Value(0.1)));
        Assert.False(RealSet.Compare(RealKind.Single, ComparisonKind.LessThanOrEqual, Value(0.1))!.Contains(Value(0.1f)));
        Assert.True(RealSet.Compare(RealKind.Single, ComparisonKind.Equal, Value(16777217))!.Contains(Value(16777216f)));
    }

    [Fact]
    public void DoubleComparisonsPreserveSinglePrecisionAndNonfiniteBehavior()
    {
        var values = Doubles();
        var bounds = values.Select(value => Value(value)).Concat(new[] { Value(0.1f), Value(float.MaxValue), Value(long.MaxValue), Value(ulong.MaxValue) });

        foreach(var kind in Enum.GetValues<ComparisonKind>())
        {
            foreach(var bound in bounds)
            {
                var set = RealSet.Compare(RealKind.Double, kind, bound)!;

                foreach(var value in values)
                {
                    Assert.Equal(Compare(value, kind, Convert.ToDouble(bound.Value)), set.Contains(Value(value)));
                }
            }
        }

        Assert.True(RealSet.Compare(RealKind.Double, ComparisonKind.GreaterThan, Value(0.1))!.Contains(Value((double)0.1f)));
        Assert.False(RealSet.Compare(RealKind.Double, ComparisonKind.Equal, Value(0.1f))!.Contains(Value(0.1)));
    }

    [Fact]
    public void DecimalComparisonsRetainStrictEndpointsWithoutBinaryConversion()
    {
        var values = Decimals();
        var bounds = values.Select(value => Value(value)).Concat(new[] { Value(long.MinValue), Value(ulong.MaxValue) });

        foreach(var kind in Enum.GetValues<ComparisonKind>())
        {
            foreach(var bound in bounds)
            {
                var set = RealSet.Compare(RealKind.Decimal, kind, bound)!;

                foreach(var value in values)
                {
                    Assert.Equal(Compare(value, kind, Convert.ToDecimal(bound.Value)), set.Contains(Value(value)));
                }
            }
        }

        Assert.Null(RealSet.Compare(RealKind.Decimal, ComparisonKind.GreaterThan, Value(0.1)));
        Assert.Null(RealSet.Compare(RealKind.Double, ComparisonKind.GreaterThan, Value(0.1m)));
        Assert.Equal(RealSet.Nothing(RealKind.Decimal), RealSet.Compare(RealKind.Decimal, ComparisonKind.GreaterThan, Value(decimal.MaxValue)));
        Assert.Equal(RealSet.Nothing(RealKind.Decimal), RealSet.Compare(RealKind.Decimal, ComparisonKind.LessThan, Value(decimal.MinValue)));
    }

    [Fact]
    public void BooleanSetOperationsPreserveMembershipAndComplements()
    {
        CheckSets(RealKind.Single, Singles().Select(value => Value(value)).ToArray(), new[] { Value(-1f), Value(-0f), Value(0.1), Value(float.MaxValue), Value(float.NaN), Value(float.PositiveInfinity) });
        CheckSets(RealKind.Double, Doubles().Select(value => Value(value)).ToArray(), new[] { Value(-1.0), Value(-0.0), Value(0.1), Value(double.MaxValue), Value(double.NaN), Value(double.PositiveInfinity) });
        CheckSets(RealKind.Decimal, Decimals().Select(value => Value(value)).ToArray(), new[] { Value(-1m), Value(0m), Value(0.1m), Value(decimal.MaxValue) });
    }

    [Fact]
    public void FailedOrderedGuardsIncludeNaNAndCannotImplyTheOppositeOrderedGuard()
    {
        foreach(var kind in new[] { RealKind.Single, RealKind.Double })
        {
            var positive = RealSet.Compare(kind, ComparisonKind.GreaterThan, Value(0))!;
            var nonpositive = RealSet.Compare(kind, ComparisonKind.LessThanOrEqual, Value(0))!;
            Assert.True(positive.Not().IncludesNaN);
            Assert.True(positive.Not().IncludesNegativeInfinity);
            Assert.False(positive.Not().IncludesPositiveInfinity);
            Assert.False(positive.Not().IsSubsetOf(nonpositive));
            Assert.True(nonpositive.IsSubsetOf(positive.Not()));
            Assert.False(positive.IsSubsetOf(RealSet.Compare(kind, ComparisonKind.LessThan, Value(double.PositiveInfinity))!));
            Assert.Equal(RealSet.Everything(kind), RealSet.Compare(kind, ComparisonKind.Equal, Value(double.NaN))!.Not());
        }
    }

    [Fact]
    public void SignedZerosNameTheSameComparisonPoint()
    {
        Assert.Equal(RealSet.Exactly(RealKind.Single, Value(0f)), RealSet.Exactly(RealKind.Single, Value(-0f)));
        Assert.Equal(RealSet.Exactly(RealKind.Double, Value(0.0)), RealSet.Exactly(RealKind.Double, Value(-0.0)));
        Assert.Equal(RealSet.Exactly(RealKind.Decimal, Value(0m)), RealSet.Exactly(RealKind.Decimal, Value(new decimal(0, 0, 0, true, 28))));
        Assert.Equal(RealSet.Exactly(RealKind.Double, Value(0.0))!.GetHashCode(), RealSet.Exactly(RealKind.Double, Value(-0.0))!.GetHashCode());
        Assert.False(RealSet.Compare(RealKind.Double, ComparisonKind.GreaterThan, Value(-0.0))!.Contains(Value(0.0)));
    }

    [Fact]
    public void AdjacentBinaryValuesMergeButAnExcludedPointStaysExcluded()
    {
        var one = RealSet.Exactly(RealKind.Double, Value(1.0))!;
        var next = RealSet.Exactly(RealKind.Double, Value(double.BitIncrement(1.0)))!;
        _ = Assert.Single(one.Or(next).Intervals);
        Assert.Equal(2, one.Not().Intervals.Length);
        Assert.False(one.Not().Contains(Value(1.0)));
        Assert.True(one.Not().Contains(Value(double.BitDecrement(1.0))));
        Assert.True(one.Not().Contains(Value(double.BitIncrement(1.0))));
    }

    [Fact]
    public void PromotionPreservesFactsAndUnsupportedDynamicConversionsAreUnknown()
    {
        var single = RealSet.Exactly(RealKind.Single, Value(0.1f))!;
        var promoted = single.ConvertedTo(RealKind.Double)!;
        Assert.True(promoted.Contains(Value((double)0.1f)));
        Assert.False(promoted.Contains(Value(0.1)));
        Assert.Null(promoted.ConvertedTo(RealKind.Single));
        Assert.Null(single.ConvertedTo(RealKind.Decimal));
        Assert.Null(RealSet.Everything(RealKind.Decimal).ConvertedTo(RealKind.Double));
        Assert.Null(RealSet.Exactly(RealKind.Single, Value(0.1)));
        Assert.True(RealSet.Exactly(RealKind.Single, Value(float.NaN))!.ConvertedTo(RealKind.Double)!.IncludesNaN);
        Assert.False(single.IsSubsetOf(promoted));
    }

    [Fact]
    public void WideningIsMonotoneAndConvergesWithoutDroppingExceptionalValues()
    {
        foreach(var kind in Enum.GetValues<RealKind>())
        {
            var previous = Between(kind, 0, 1);
            var current = Between(kind, -1, 2);
            var widened = current.WidenedAgainst(previous);
            Assert.True(previous.IsSubsetOf(widened));
            Assert.True(current.IsSubsetOf(widened));
            Assert.Equal(widened, Between(kind, -2, 3).WidenedAgainst(widened));
            Assert.Equal(widened, widened.WidenedAgainst(widened));
            Assert.True(RealSet.Nothing(kind).WidenedAgainst(previous).Equals(previous));

            if(kind != RealKind.Decimal)
            {
                var exceptional = RealSet.Exactly(kind, kind == RealKind.Single ? Value(float.NaN) : Value(double.NaN))!;
                var joined = exceptional.WidenedAgainst(widened);
                Assert.True(joined.IncludesNaN);
                Assert.True(widened.IsSubsetOf(joined));
                Assert.True(exceptional.IsSubsetOf(joined));
                Assert.Equal(joined, widened.WidenedAgainst(joined));
            }
        }
    }

    private static void CheckSets(RealKind kind, LiteralValue[] values, LiteralValue[] bounds)
    {
        var sets = new[] { RealSet.Everything(kind), RealSet.Nothing(kind) }
            .Concat(bounds.SelectMany(bound => Enum.GetValues<ComparisonKind>().Select(comparison => RealSet.Compare(kind, comparison, bound)!))).ToArray();

        foreach(var left in sets)
        {
            Assert.Equal(left, left.Not().Not());
            Assert.Equal(RealSet.Everything(kind), left.Or(left.Not()));
            Assert.Equal(RealSet.Nothing(kind), left.And(left.Not()));

            foreach(var right in sets)
            {
                var union = left.Or(right);
                var intersection = left.And(right);
                Assert.True(left.IsSubsetOf(union));
                Assert.True(intersection.IsSubsetOf(left));

                foreach(var value in values)
                {
                    Assert.Equal(left.Contains(value) || right.Contains(value), union.Contains(value));
                    Assert.Equal(left.Contains(value) && right.Contains(value), intersection.Contains(value));
                    Assert.Equal(!left.Contains(value), left.Not().Contains(value));
                    Assert.False(left.IsSubsetOf(right) && left.Contains(value) && !right.Contains(value));
                }
            }
        }
    }

    private static RealSet Between(RealKind kind, int low, int high)
    {
        return RealSet.Compare(kind, ComparisonKind.GreaterThanOrEqual, Value(low))!
            .And(RealSet.Compare(kind, ComparisonKind.LessThanOrEqual, Value(high))!);
    }

    private static float[] Singles()
    {
        return [float.NegativeInfinity, -float.MaxValue, float.BitIncrement(-float.MaxValue), -16777218f, -1f,
            -float.Epsilon, -0f, 0f, float.Epsilon, float.BitDecrement(0.1f), 0.1f, float.BitIncrement(0.1f),
            float.BitDecrement(1f), 1f, float.BitIncrement(1f), 16777216f, 16777218f,
            float.BitDecrement(float.MaxValue), float.MaxValue, float.PositiveInfinity, float.NaN];
    }

    private static double[] Doubles()
    {
        return [double.NegativeInfinity, -double.MaxValue, double.BitIncrement(-double.MaxValue), -1.0,
            -double.Epsilon, -0.0, 0.0, double.Epsilon, double.BitDecrement(0.1), 0.1, double.BitIncrement(0.1),
            (double)0.1f, double.BitDecrement(1.0), 1.0, double.BitIncrement(1.0),
            double.BitDecrement(double.MaxValue), double.MaxValue, double.PositiveInfinity, double.NaN];
    }

    private static decimal[] Decimals()
    {
        return [decimal.MinValue, decimal.MinValue + 1, -1m, -0.0000000000000000000000000001m, 0m,
            0.0000000000000000000000000001m, 0.1m, 0.9999999999999999999999999999m, 1m,
            1.0000000000000000000000000001m, decimal.MaxValue - 1, decimal.MaxValue];
    }

    private static LiteralValue Value(object value)
    {
        var name = value switch
        {
            float => WellKnownTypes.Single.FullyQualifiedName,
            double => WellKnownTypes.Double.FullyQualifiedName,
            decimal => WellKnownTypes.Decimal.FullyQualifiedName,
            uint => WellKnownTypes.UInt32.FullyQualifiedName,
            long => WellKnownTypes.Int64.FullyQualifiedName,
            ulong => WellKnownTypes.UInt64.FullyQualifiedName,
            _ => WellKnownTypes.Int32.FullyQualifiedName,
        };
        return new LiteralValue(value, name);
    }

    private static bool Compare(double left, ComparisonKind kind, double right)
    {
        return kind switch
        {
            ComparisonKind.Equal => left == right,
            ComparisonKind.LessThan => left < right,
            ComparisonKind.LessThanOrEqual => left <= right,
            ComparisonKind.GreaterThan => left > right,
            ComparisonKind.GreaterThanOrEqual => left >= right,
            _ => false,
        };
    }

    private static bool Compare(decimal left, ComparisonKind kind, decimal right)
    {
        return kind switch
        {
            ComparisonKind.Equal => left == right,
            ComparisonKind.LessThan => left < right,
            ComparisonKind.LessThanOrEqual => left <= right,
            ComparisonKind.GreaterThan => left > right,
            ComparisonKind.GreaterThanOrEqual => left >= right,
            _ => false,
        };
    }
}