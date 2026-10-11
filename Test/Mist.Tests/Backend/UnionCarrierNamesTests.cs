using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class UnionCarrierNamesTests
{
    private static readonly ConstructedType Three = ArrayOf(IntegerType, 3);
    private static readonly ConstructedType Five = ArrayOf(IntegerType, 5);

    [Theory]
    [InlineData(3L, "Int32Array3")]
    [InlineData(0L, "Int32Array0")]
    [InlineData(null, "Int32Array")]
    public void AnArrayConstituent_IsNamedForItsLength(long? length, string expected)
    {
        Assert.Equal(expected, UnionCarrierNames.ConstituentName(ArrayOf(IntegerType, length)));
    }

    [Fact]
    public void ANestedArray_NamesTheLengthAtEveryLevel()
    {
        Assert.Equal("Int32Array3Array2", UnionCarrierNames.ConstituentName(ArrayOf(Three, 2)));
        Assert.Equal("Int32ArrayArray2", UnionCarrierNames.ConstituentName(ArrayOf(ArrayOf(IntegerType, null), 2)));
        Assert.Equal("Int32Array3Array", UnionCarrierNames.ConstituentName(ArrayOf(Three, null)));
    }

    [Fact]
    public void ALiftedArgument_IsNamedForItsValue()
    {
        Assert.Equal("Box_1_Const_5", UnionCarrierNames.ConstituentName(BoxOf(5)));
        Assert.Equal("Box_1_Const_Neg5", UnionCarrierNames.ConstituentName(BoxOf(-5)));
        Assert.NotEqual(UnionCarrierNames.ConstituentName(BoxOf(5)), UnionCarrierNames.ConstituentName(BoxOf(6)));
    }

    [Fact]
    public void ARefinement_StillErasesToTheConstituentItRefines()
    {
        Assert.Equal("Int32Array3", UnionCarrierNames.ConstituentName(HandBuiltModel.Refined(Three, "NonEmpty")));
    }

    [Fact]
    public void UnionsDifferingOnlyInALength_AreDistinctCarriers()
    {
        Assert.Equal("Union_Int32Array3_Int32Array5", Carrier(Three, Five));
        Assert.Equal("Union_Int32Array3_Int32Array4", Carrier(Three, ArrayOf(IntegerType, 4)));
        Assert.NotEqual(Carrier(Three, StringType), Carrier(Five, StringType));
    }

    [Fact]
    public void ConstituentOrder_DoesNotReachTheCarrierName()
    {
        Assert.Equal(Carrier(Three, Five), Carrier(Five, Three));
        Assert.Equal(Carrier(Three, Five, StringType), Carrier(StringType, Five, Three));
    }

    [Fact]
    public void TheAccessorsAndTheFactory_AreNamedForOneConstituent()
    {
        Assert.Equal("AsInt32Array3OrNull", UnionCarrierNames.NarrowingAccessor(Three));
        Assert.Equal("FromInt32Array3", UnionCarrierNames.EntryFactory(Three));
        Assert.Equal("FromInt32Array5", UnionCarrierNames.EntryFactory(Five));
    }

    private static string Carrier(params IMistType[] constituents)
    {
        return UnionCarrierNames.CarrierName(new UnionType(constituents));
    }
}