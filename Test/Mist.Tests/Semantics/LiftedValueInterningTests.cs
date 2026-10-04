using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// One interned lifted type per value: the box an integral value is held in is chosen by the value, never
/// by the overload a caller reaches for, so a length lifted as a long and the same length lifted as a
/// ulong close one construction, and only ulong's upper half keeps a box of its own.
/// </summary>
[Trait("Tier", "1")]
public sealed class LiftedValueInterningTests
{
    private static readonly ClassType Int32 = new("Int32", "Standard", new Scope(null), null, null);

    [Theory]
    [InlineData(0UL)]
    [InlineData(3UL)]
    [InlineData(9223372036854775807UL)]
    public void AULongALongHolds_IsTheLongsLiftedType(ulong value)
    {
        var interner = new TypeInterner();

        var asULong = interner.LiftLiteral(Int32, value);

        Assert.Same(interner.LiftLiteral(Int32, (long)value), asULong);
        _ = Assert.IsType<long>(asULong.Value);
    }

    [Fact]
    public void ULongsUpperHalf_KeepsAULongBoxDistinctFromItsWrappedLong()
    {
        var interner = new TypeInterner();

        var top = interner.LiftLiteral(Int32, ulong.MaxValue);

        Assert.Equal(ulong.MaxValue, Assert.IsType<ulong>(top.Value));
        Assert.Same(top, interner.LiftLiteral(Int32, ulong.MaxValue));
        Assert.NotSame(top, interner.LiftLiteral(Int32, -1L));
    }

    [Fact]
    public void ABoolOrACharIsNeverTheIntegerOfTheSameNumber()
    {
        var interner = new TypeInterner();
        var boolean = new ClassType("Boolean", "Standard", new Scope(null), null, null);
        var character = new ClassType("Char", "Standard", new Scope(null), null, null);

        Assert.NotSame(interner.LiftLiteral(Int32, 1L), interner.LiftLiteral(boolean, true));
        Assert.NotSame(interner.LiftLiteral(Int32, 65L), interner.LiftLiteral(character, 'A'));
    }
}