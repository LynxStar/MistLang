using Mist.Backends.CSharpTranspiler.Lowering;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ValueTupleNestingTests
{
    [Theory]
    [InlineData(0, "System.ValueTuple")]
    [InlineData(1, "System.ValueTuple<T1>")]
    [InlineData(7, "System.ValueTuple<T1, T2, T3, T4, T5, T6, T7>")]
    [InlineData(8, "System.ValueTuple<T1, T2, T3, T4, T5, T6, T7, System.ValueTuple<T8>>")]
    [InlineData(9, "System.ValueTuple<T1, T2, T3, T4, T5, T6, T7, System.ValueTuple<T8, T9>>")]
    [InlineData(14, "System.ValueTuple<T1, T2, T3, T4, T5, T6, T7, System.ValueTuple<T8, T9, T10, T11, T12, T13, T14>>")]
    [InlineData(15,
        "System.ValueTuple<T1, T2, T3, T4, T5, T6, T7, System.ValueTuple<T8, T9, T10, T11, T12, T13, T14, System.ValueTuple<T15>>>")]
    public void Spelled_NestsEverythingPastTheSeventhUnderRest(int width, string expected)
    {
        Assert.Equal(expected, ValueTupleNesting.Spelled([.. Enumerable.Range(1, width).Select(i => $"T{i}")]));
    }

    [Theory]
    [InlineData(0, "Item1")]
    [InlineData(6, "Item7")]
    [InlineData(7, "Rest.Item1")]
    [InlineData(8, "Rest.Item2")]
    [InlineData(13, "Rest.Item7")]
    [InlineData(14, "Rest.Rest.Item1")]
    public void Path_FollowsTheRestChainToAnElement(int position, string expected)
    {
        Assert.Equal(expected, ValueTupleNesting.Path(position));
    }
}