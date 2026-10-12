using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class UnionCarrierNamesTests
{
    private readonly HandBuiltRefinements h = new();
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

    [Theory]
    [InlineData("small", "Int32_Small_Lt10")]
    [InlineData("big", "Int32_Big_Gt100")]
    [InlineData("negative", "Int32_Split_LtNeg100")]
    [InlineData("conjunction", "Int32_Percent_Ge0AndLe100")]
    [InlineData("disjunction-of-a-negation", "Int32_Edge_NotEq0OrGe5")]
    [InlineData("anonymous", "Int32_Where7_Gt0")]
    [InlineData("character", "Char_Lower_GeChar97")]
    [InlineData("truth", "Boolean_Yes_EqTrue")]
    [InlineData("real", "Double_Ratio_Gt0p5")]
    [InlineData("negative-real", "Double_Debt_LtNeg2p5")]
    [InlineData("refinement-of-a-refinement", "Int32_Positive_Gt0_SmallPositive_Lt10")]
    [InlineData("sized-base", "Int32Array3_Unbound_X")]
    public void ARefinement_IsNamedForItsBaseItsNameAndItsPredicate(string shape, string expected)
    {
        var name = UnionCarrierNames.Spelled(Refinement(shape), keepsRefinements: true);

        Assert.True(expected.EndsWith("_X", StringComparison.Ordinal) ? name.StartsWith(expected, StringComparison.Ordinal) && name.Length == expected.Length + 8 : name == expected,
            $"{shape}: {name}");
    }

    [Fact]
    public void RefinementsOfOneBase_AreDistinctConstituentsOfOneCarrier()
    {
        var small = Refinement("small");
        var big = Refinement("big");

        Assert.Equal("Union_Int32_Big_Gt100_Int32_Small_Lt10", Carrier(small, big));
        Assert.Equal(Carrier(small, big), Carrier(big, small));
        Assert.NotEqual(Carrier(small, big), Carrier(small, IntegerType));
        Assert.Equal("AsInt32_Small_Lt10OrNull", UnionCarrierNames.NarrowingAccessor(small, new UnionType([small, big])));
        Assert.Equal("FromInt32_Big_Gt100", UnionCarrierNames.EntryFactory(big, new UnionType([small, big])));
    }

    [Fact]
    public void ARefinementErases_WhereItsBaseAlreadyTellsTheConstituentsApart()
    {
        var small = Refinement("small");
        var young = h.Between(IntegerType, 3L, 8L, "AgeRange");
        var bounded = h.Refine(PropertyPatternEmissionTests.Type("Byte", "Standard"), ComparisonKind.LessThan, 64L, "AgeRange");

        Assert.Equal("Int32", UnionCarrierNames.ConstituentName(small, new UnionType([small, StringType])));
        Assert.Equal("Union_Byte_Int32", Carrier(young, bounded));
        Assert.Equal("Union_Int32_String", Carrier(small, StringType));
        Assert.Equal("Union_Int32_Int32_Small_Lt10", Carrier(small, IntegerType));
    }

    [Fact]
    public void RefinementsSharingTheirAliassName_AreToldApartByTheirPredicates()
    {
        var below = h.Refine(IntegerType, ComparisonKind.LessThan, -100L, "Split");
        var above = h.Refine(IntegerType, ComparisonKind.GreaterThan, 100L, "Split");

        Assert.Equal("Union_Int32_Split_Gt100_Int32_Split_LtNeg100", Carrier(below, above));
    }

    [Fact]
    public void AnUnspelledPredicate_IsNamedByADigestOfItsDeclaration_SharedByEverySubstitution()
    {
        var length = new TypeParameter("n", 0, new Scope(null), null) { IsDependent = true, Domain = IntegerType };
        var below = new RefinedType(IntegerType, Declared("< n"), "Below")
        {
            Predicate = h.Compare(IntegerType, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), length)),
        };
        var other = new RefinedType(IntegerType, Declared("`[0-9]+`"), "Below")
        {
            Predicate = new RegexPattern(HandBuiltModel.Node(), IntegerType, "[0-9]+"),
        };
        var anonymous = new RefinedType(IntegerType, HandBuiltModel.Node(3), null) { Predicate = below.Predicate };
        Dictionary<TypeParameter, IMistType> closing(long value)
        {
            return new() { [length] = new LiftedType(IntegerType, value) };
        }

        static string named(IMistType type)
        {
            return UnionCarrierNames.Spelled(type, keepsRefinements: true);
        }

        var five = named(new RefinedType(below, IntegerType, closing(5)));

        Assert.Matches("^Int32_Below_X[0-9A-F]{8}$", five);
        Assert.Equal(five, named(new RefinedType(below, IntegerType, closing(5))));
        Assert.NotEqual(five, named(new RefinedType(below, IntegerType, closing(6))));
        Assert.NotEqual(named(below), named(other));
        Assert.Matches("^Int32_Where3_X[0-9A-F]{8}$", named(anonymous));
    }

    private static TrueLiteralExpressionPrimaryNode Declared(string text)
    {
        return new TrueLiteralExpressionPrimaryNode
        {
            Span = new Storm.Runtime.SourceSpan(0, text.Length),
            Origin = new ParsedOrigin(new SourceText("declared.mist", text)),
        };
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
        var sized = new UnionType([Three, Five]);

        Assert.Equal("AsInt32Array3OrNull", UnionCarrierNames.NarrowingAccessor(Three, sized));
        Assert.Equal("FromInt32Array3", UnionCarrierNames.EntryFactory(Three, sized));
        Assert.Equal("FromInt32Array5", UnionCarrierNames.EntryFactory(Five, sized));
    }

    private RefinedType Refinement(string shape)
    {
        var character = PropertyPatternEmissionTests.Type("Char", "Standard");
        var real = PropertyPatternEmissionTests.Type("Double", "Standard");
        var positive = h.Refine(IntegerType, ComparisonKind.GreaterThan, 0L, "Positive");
        Pattern compare(IMistType type, ComparisonKind kind, object bound)
        {
            return h.Compare(type, kind, bound is double or char or bool ? h.Literal(bound, type) : bound);
        }

        return shape switch
        {
            "small" => h.Refine(IntegerType, ComparisonKind.LessThan, 10L, "Small"),
            "big" => h.Refine(IntegerType, ComparisonKind.GreaterThan, 100L, "Big"),
            "negative" => h.Refine(IntegerType, ComparisonKind.LessThan, -100L, "Split"),
            "conjunction" => h.Between(IntegerType, 0L, 100L, "Percent"),
            "disjunction-of-a-negation" => HandBuiltRefinements.RefinePattern(IntegerType, new DisjunctionPattern(HandBuiltModel.Node(), IntegerType,
                [new NegatedPattern(HandBuiltModel.Node(), IntegerType, compare(IntegerType, ComparisonKind.Equal, 0L)), compare(IntegerType, ComparisonKind.GreaterThanOrEqual, 5L)]), "Edge"),
            "anonymous" => new RefinedType(IntegerType, HandBuiltModel.Node(7), null) { Predicate = compare(IntegerType, ComparisonKind.GreaterThan, 0L) },
            "character" => HandBuiltRefinements.RefinePattern(character, compare(character, ComparisonKind.GreaterThanOrEqual, 'a'), "Lower"),
            "truth" => HandBuiltRefinements.RefinePattern(BooleanType, compare(BooleanType, ComparisonKind.Equal, true), "Yes"),
            "real" => HandBuiltRefinements.RefinePattern(real, compare(real, ComparisonKind.GreaterThan, 0.5), "Ratio"),
            "negative-real" => HandBuiltRefinements.RefinePattern(real, compare(real, ComparisonKind.LessThan,
                new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, h.Literal(2.5, real), real)), "Debt"),
            "refinement-of-a-refinement" => h.Refine(positive, ComparisonKind.LessThan, 10L, "SmallPositive"),
            "sized-base" => HandBuiltModel.Refined(Three, "Unbound"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private static string Carrier(params IMistType[] constituents)
    {
        return UnionCarrierNames.CarrierName(new UnionType(constituents));
    }
}