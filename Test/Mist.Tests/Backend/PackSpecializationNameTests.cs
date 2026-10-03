using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 1: what a closed pack contributes to the names an instance and a union carrier are emitted under,
// and that a pack is seen through when asking whether a type is still open. A pack argument named by its
// domain alone gives every width and every element list one name, so two instances or two carriers
// collide into one C# declaration; a pack not seen through leaves an open union carried as if closed.
[Trait("Tier", "1")]
public sealed class PackSpecializationNameTests
{
    private readonly HandBuiltModel built = new();

    private readonly TypeParameter pack = new("Ts", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    [Fact]
    public void AnInstanceIsNamedForItsPacksWidthAndEachElement()
    {
        Assert.Equal(
            "Duo__P2__Standard_Int32__Standard_String",
            UnionSpecialization.Name("Duo", [Pack(built.Type("Int32"), built.Type("String"))]));
    }

    [Fact]
    public void PacksSplitDifferently_NameDifferentInstances()
    {
        var int32 = built.Type("Int32");
        var text = built.Type("String");

        Assert.NotEqual(
            UnionSpecialization.Name("Two", [Pack(int32), Pack(text)]),
            UnionSpecialization.Name("Two", [Pack(int32, text), Pack()]));
    }

    [Fact]
    public void ACarrierOverAVariadicConstruction_IsNamedForWhatItsPackHolds()
    {
        var inner = HandBuiltModel.Class("Inner");
        var text = built.Type("String");
        var two = new UnionType([new ConstructedType(inner, [Pack(built.Type("Int32"), built.Type("Boolean"))]), text]);
        var one = new UnionType([new ConstructedType(inner, [Pack(built.Type("Char"))]), text]);

        Assert.Equal("Union_Inner_1_Pack_2_Int32_Boolean_String", UnionCarrierNames.CarrierName(two));
        Assert.NotEqual(UnionCarrierNames.CarrierName(two), UnionCarrierNames.CarrierName(one));
    }

    [Theory]
    [InlineData("spread-in-a-pack", true)]
    [InlineData("element-in-a-pack", true)]
    [InlineData("spread-in-a-tuple", true)]
    [InlineData("closed-pack", false)]
    public void APackIsSeenThroughWhenAskingWhetherAUnionIsStillOpen(string shape, bool open)
    {
        var inner = HandBuiltModel.Class("Inner");
        IMistType argument = shape switch
        {
            "spread-in-a-pack" => Pack(new SpreadType(pack)),
            "element-in-a-pack" => Pack(pack.ElementAt(0)),
            "spread-in-a-tuple" => new TupleType([new SpreadType(pack)]),
            _ => Pack(built.Type("Int32")),
        };
        var union = new UnionType([new ConstructedType(inner, [argument]), built.Type("String")]);

        Assert.Equal(open, CSharpEmitterV2.IsOpen(union));
        Assert.Equal(open, TypeParameterReach.Collect(union).Count > 0);
    }

    private LiftedType Pack(params IMistType[] elements)
    {
        return new LiftedType(built.Type("Type"), new TypePackValue(elements));
    }
}