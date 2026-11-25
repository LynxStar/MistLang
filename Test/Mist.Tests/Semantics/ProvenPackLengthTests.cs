using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ProvenPackLengthTests
{
    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(2L, 2)]
    [InlineData((long)int.MaxValue, int.MaxValue)]
    [InlineData(-1L, null)]
    [InlineData(int.MaxValue + 1L, null)]
    public void ATypeArrayProvesALengthOnlyWithinTheLengthsAnArrayCanHave(long length, int? proven)
    {
        Assert.Equal(proven, PackTypePositions.ProvenLength(Array(built.Type("Type"), built.Interner.LiftLiteral(built.Type("Int32"), length))));
    }

    [Fact]
    public void AFixedSizeArrayOfAnythingButTypeProvesNoPackLength()
    {
        Assert.Null(PackTypePositions.ProvenLength(Array(built.Type("Int32"), built.Interner.LiftLiteral(built.Type("Int32"), 2L))));
    }

    [Fact]
    public void ATypeArrayOfUnknownLengthProvesNoPackLength()
    {
        Assert.Null(PackTypePositions.ProvenLength(Array(built.Type("Type"), built.Interner.GetOrCreateUnknownValue(built.Type("Int32")))));
    }

    private IMistType Array(IMistType element, IMistType length)
    {
        return built.Interner.GetOrCreateConstructedType(built.Type("Array<,>"), [element, length]);
    }
}