using Mist.Backends.CSharpTranspiler.AmbientProvider;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ClrParameterNullabilityTests
{
    [Fact]
    public void IFormattableToString_DeclaresBothParametersNullable()
    {
        Assert.Equal([true, true], Read(typeof(IFormattable), nameof(IFormattable.ToString)));
    }

    [Theory]
    [InlineData(typeof(IComparable<string>), true)]
    [InlineData(typeof(IComparable<int>), false)]
    [InlineData(typeof(IEquatable<>), true)]
    [InlineData(typeof(IEquatable<object>), true)]
    [InlineData(typeof(IEquatable<Guid>), false)]
    public void AGenericSlotsParameter_IsNullableOnlyWhereItsTypeCanHoldNull(Type slotOwner, bool expected)
    {
        Assert.Equal([expected], Read(slotOwner, slotOwner.GetMethods().Single().Name));
    }

    [Fact]
    public void EachParameter_IsReadFromItsOwnFlagAndOnlyTheOutermostTypeCounts()
    {
        Assert.Equal([true, false, false, true, false, true], Read(typeof(ISlots), nameof(ISlots.Mixed)));
    }

    [Fact]
    public void AParameterWithoutAFlag_TakesItsMembersContext()
    {
        Assert.Equal([true, true], Read(typeof(ISlots), nameof(ISlots.AllNullable)));
        Assert.Equal([false], Read(typeof(ISlots), nameof(ISlots.Plain)));
    }

    [Fact]
    public void AnObliviousParameter_IsNotNullable()
    {
        Assert.Equal([false], Read(typeof(IOblivious), nameof(IOblivious.Old)));
    }

    [Fact]
    public void ANonNullBclParameter_IsNotNullable()
    {
        Assert.Equal([false, false], Read(typeof(Path), nameof(Path.Combine), typeof(string), typeof(string)));
    }

    private static bool[] Read(Type type, string name, params Type[] signature)
    {
        var method = signature.Length == 0 ? type.GetMethod(name)! : type.GetMethod(name, signature)!;

        return [.. method.GetParameters().Select(ClrNullability.ParameterIsNullable)];
    }

    internal interface ISlots
    {
        void Mixed(string? maybe, string sure, int count, ref string? alias, List<string?> inner, List<string>? outer);

        void AllNullable(string? text, object? value);

        void Plain(string text);
    }

#nullable disable
    internal interface IOblivious
    {
        void Old(string text);
    }
#nullable restore
}