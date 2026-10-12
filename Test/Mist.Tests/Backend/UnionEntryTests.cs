using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class UnionEntryTests
{
    private static readonly ConstructedType Three = ArrayOf(IntegerType, 3);
    private static readonly ConstructedType Five = ArrayOf(IntegerType, 5);
    private static readonly ConstructedType Unsized = ArrayOf(IntegerType, null);
    private static readonly UnionType Sized = new([Three, Five]);

    [Fact]
    public void OnlyConstituentsSharingACSharpSpelling_ShareIt()
    {
        var mixed = new UnionType([Three, Five, StringType]);

        Assert.True(UnionEntry.SharesSpelling(Sized, Three));
        Assert.True(UnionEntry.SharesSpelling(mixed, Five));
        Assert.False(UnionEntry.SharesSpelling(mixed, StringType));
        Assert.False(UnionEntry.SharesSpelling(new UnionType([IntegerType, StringType]), IntegerType));
    }

    [Fact]
    public void ANullableAnnotation_DoesNotMakeTwoSpellingsDistinct()
    {
        var list = new ClassType("List<>", "System.Collections.Generic", new Scope(null), null, null!);
        var annotated = new ConstructedType(list, [OptionalOf(StringType)]);
        var plain = new ConstructedType(list, [StringType]);

        Assert.True(UnionEntry.SharesSpelling(new UnionType([annotated, plain]), plain));
    }

    [Fact]
    public void ASizedValue_EntersThroughTheFactoryOfItsOwnLength()
    {
        Assert.Equal("Union_Int32Array3_Int32Array5.FromInt32Array3", UnionEntry.Factory(Three, Sized));
        Assert.Equal("Union_Int32Array3_Int32Array5.FromInt32Array5", UnionEntry.Factory(Five, Sized));
    }

    [Fact]
    public void AnAlias_IsSeenThroughOnEitherSide()
    {
        Assert.Equal("Union_Int32Array3_Int32Array5.FromInt32Array3", UnionEntry.Factory(AliasOf("Three", Three), Sized));
        Assert.Equal("Union_Int32Array3_Int32Array5.FromInt32Array5", UnionEntry.Factory(Five, AliasOf("Sized", Sized)));
    }

    [Fact]
    public void AConstituentWithASpellingOfItsOwn_KeepsTheImplicitOperator()
    {
        Assert.Null(UnionEntry.Factory(StringType, new UnionType([Three, Five, StringType])));
        Assert.Null(UnionEntry.Factory(IntegerType, new UnionType([IntegerType, StringType])));
    }

    [Fact]
    public void AnIdenticalConstituent_IsChosenOverOneThatContainsIt()
    {
        var either = new UnionType([Three, Unsized]);

        Assert.Equal("Union_Int32Array_Int32Array3.FromInt32Array3", UnionEntry.Factory(Three, either));
        Assert.Equal("Union_Int32Array_Int32Array3.FromInt32Array", UnionEntry.Factory(Unsized, either));
    }

    [Fact]
    public void AValueNoSingleConstituentClaims_IsLeftUnlowered()
    {
        Assert.Null(UnionEntry.Factory(Unsized, Sized));
    }

    [Fact]
    public void ATupleValue_IsRebuiltOnlyAroundTheElementsThatEnterThroughAFactory()
    {
        Assert.Equal("(Union_Int32Array3_Int32Array5.FromInt32Array3(t.Item1), t.Item2)",
            UnionEntry.Decomposed(TupleOf(Three, StringType), TupleOf(Sized, StringType), "t"));
        Assert.Equal("((Union_Int32Array3_Int32Array5.FromInt32Array5(t.Item1.Item1), t.Item1.Item2), t.Item2)",
            UnionEntry.Decomposed(TupleOf(TupleOf(Five, IntegerType), StringType), TupleOf(TupleOf(Sized, IntegerType), StringType), "t"));
        Assert.Null(UnionEntry.Decomposed(TupleOf(IntegerType, StringType), TupleOf(new UnionType([IntegerType, StringType]), StringType), "t"));
        Assert.Null(UnionEntry.Decomposed(TupleOf(Three, StringType), TupleOf(Three, StringType), "t"));
    }

    [Fact]
    public void ARefinedValue_EntersThroughTheFactoryOfItsOwnRefinement()
    {
        var (small, big) = Refinements();
        var either = new UnionType([big, small]);
        const string carrier = "Union_Int32_Big_Gt100_Int32_Small_Lt10";

        Assert.Equal($"{carrier}.FromInt32_Small_Lt10", UnionEntry.Factory(small, either));
        Assert.Equal($"{carrier}.FromInt32_Big_Gt100", UnionEntry.Factory(big, new UnionType([small, big])));
        Assert.Equal($"{carrier}.FromInt32_Small_Lt10", UnionEntry.Factory(AliasOf("Tiny", small), AliasOf("Either", either)));
    }

    [Fact]
    public void ARefinementBesideItsOwnBase_IsChosenByIdentity()
    {
        var (small, _) = Refinements();
        var beside = new UnionType([IntegerType, small]);

        Assert.Equal("Union_Int32_Int32_Small_Lt10.FromInt32_Small_Lt10", UnionEntry.Factory(small, beside));
        Assert.Equal("Union_Int32_Int32_Small_Lt10.FromInt32", UnionEntry.Factory(IntegerType, beside));
    }

    [Fact]
    public void AnUnrefinedValue_IsGivenNoRefinementItCouldEnter()
    {
        var (small, big) = Refinements();

        Assert.Null(UnionEntry.Factory(IntegerType, new UnionType([small, big])));
    }

    [Fact]
    public void AnEnum_EntersACarrierOtherThanItsOwnAsTheValueItWraps()
    {
        var pair = new UnionType([IntegerType, StringType]);
        var color = HandBuiltModel.Enum("Color", IntegerType);
        var mixed = HandBuiltModel.Enum("Mixed", pair);
        var wider = new UnionType([BooleanType, IntegerType, StringType]);

        Assert.Same(IntegerType, UnionEntry.Unwrapped(color, pair));
        Assert.Same(pair, UnionEntry.Unwrapped(mixed, wider));
        Assert.Same(pair, UnionEntry.Unwrapped(AliasOf("Shade", mixed), AliasOf("Wider", wider)));
        Assert.Null(UnionEntry.Unwrapped(mixed, pair));
        Assert.Null(UnionEntry.Unwrapped(color, new UnionType([color, IntegerType])));
        Assert.Null(UnionEntry.Unwrapped(color, new UnionType([color, StringType])));
        Assert.Null(UnionEntry.Unwrapped(color, OptionalOf(IntegerType)));
        Assert.Null(UnionEntry.Unwrapped(color, IntegerType));
        Assert.Null(UnionEntry.Unwrapped(IntegerType, pair));
        Assert.Null(UnionEntry.Unwrapped(color, null));
    }

    [Fact]
    public void NoCarrierEntry_LowersNothing()
    {
        Assert.Null(UnionEntry.Factory(Three, null));
        Assert.Null(UnionEntry.Factory(Three, Three));
        Assert.Null(UnionEntry.Factory(Three, OptionalOf(Three)));
        Assert.Null(UnionEntry.Factory(Sized, new UnionType([Three, Five, StringType])));
    }

    private static (RefinedType Small, RefinedType Big) Refinements()
    {
        var h = new HandBuiltRefinements();
        return (h.Refine(IntegerType, ComparisonKind.LessThan, 10L, "Small"), h.Refine(IntegerType, ComparisonKind.GreaterThan, 100L, "Big"));
    }
}