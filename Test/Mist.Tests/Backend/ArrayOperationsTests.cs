using Mist.Backends.CSharpTranspiler.Runtime;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ArrayOperationsTests
{
    [Fact]
    public void IndexOfDistinguishesTheFirstMatchFromAbsence()
    {
        Assert.Equal(0, ArrayOperations.IndexOf([0, 0, 1], 0));
        Assert.Equal(1, ArrayOperations.IndexOf([2, 0, 0], 0));
        Assert.Null(ArrayOperations.IndexOf([0, 0, 1], 3));
        Assert.Null(ArrayOperations.IndexOf(Array.Empty<int>(), 0));
    }

    [Fact]
    public void ContainsUsesElementEqualityIncludingNullAndNan()
    {
        Assert.True(ArrayOperations.Contains(["value", null], null));
        Assert.True(ArrayOperations.Contains([double.NaN], double.NaN));
        Assert.True(ArrayOperations.Contains([new EqualValue(1)], new EqualValue(1)));
        Assert.False(ArrayOperations.Contains([new EqualValue(1)], new EqualValue(2)));
    }

    [Fact]
    public void FindPreservesZeroAndShortCircuitsInArrayOrder()
    {
        var visited = new List<int>();
        var result = ArrayOperations.Find([2, 0, 3, 0], value =>
        {
            visited.Add(value);
            return value == 0;
        });

        Assert.Equal(0, Assert.IsType<int>(result));
        Assert.Equal([2, 0], visited);
        Assert.Null(ArrayOperations.Find([0, 1], value => value == 2));
        Assert.Null(ArrayOperations.Find(Array.Empty<int>(), _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void FindReturnsTheFirstReferenceMatch()
    {
        var first = new EqualValue(1);
        Assert.Same(first, ArrayOperations.Find([first, new EqualValue(1)], _ => true));
    }

    [Fact]
    public void AnyShortCircuitsAndDoesNotVisitAnEmptyArray()
    {
        var visited = new List<int>();
        Assert.True(ArrayOperations.Any([2, 0, 3], value =>
        {
            visited.Add(value);
            return value == 0;
        }));
        Assert.Equal([2, 0], visited);
        Assert.False(ArrayOperations.Any([0, 1], value => value == 2));
        Assert.False(ArrayOperations.Any(Array.Empty<int>(), _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void SortMutatesTheReceiverUsingTheSuppliedComparer()
    {
        string[] values = ["b", "A", "a"];
        var same = values;
        ArrayOperations.Sort(values, StringComparer.Ordinal);
        Assert.Equal(["A", "a", "b"], same);
        int[] numbers = [2, 1, 3];
        ArrayOperations.Sort(numbers, Comparer<int>.Create((left, right) => right.CompareTo(left)));
        Assert.Equal([3, 2, 1], numbers);
    }

    private sealed record EqualValue(int Number);
}