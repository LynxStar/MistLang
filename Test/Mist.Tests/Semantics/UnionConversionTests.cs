using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class UnionConversionTests
{
    private readonly HandBuiltRefinements h = new();

    [Fact]
    public void AnEnum_ConvertsImplicitlyToTheWholeUnionItWraps_AndToAUnionHoldingIt()
    {
        var pair = new UnionType([h.Int, h.String]);
        var mixed = HandBuiltModel.Enum("Mixed", pair);

        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(mixed, pair, h.Built.Interner));
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(mixed, new UnionType([h.Bool, h.Int, h.String]), h.Built.Interner));
    }

    [Fact]
    public void AnEnumOverAUnion_DoesNotConvertToOneConstituent_ToAnUnrelatedUnion_OrBack()
    {
        var pair = new UnionType([h.Int, h.String]);
        var mixed = HandBuiltModel.Enum("Mixed", pair);

        Assert.False(ConversionClassifier.IsImplicit(mixed, h.Int, h.Built.Interner));
        Assert.False(ConversionClassifier.IsImplicit(mixed, h.String, h.Built.Interner));
        Assert.False(ConversionClassifier.IsImplicit(mixed, new UnionType([h.Int, h.Bool]), h.Built.Interner));
        Assert.False(ConversionClassifier.IsImplicit(pair, mixed, h.Built.Interner));
    }

    [Fact]
    public void AnEnumOverAPrimitive_KeepsItsConversions()
    {
        var color = HandBuiltModel.Enum("Color", h.Int);

        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(color, h.Int, h.Built.Interner));
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(color, h.Built.Type("Int64"), h.Built.Interner));
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(color, new UnionType([h.Int, h.String]), h.Built.Interner));
        Assert.False(ConversionClassifier.IsImplicit(h.Int, color, h.Built.Interner));
    }

    [Fact]
    public void AUnionOfRefinements_WidensIntoAUnionHoldingEachOfThem()
    {
        var small = h.Refine(h.Int, ComparisonKind.LessThan, 10L, "Small");
        var big = h.Refine(h.Int, ComparisonKind.GreaterThan, 100L, "Big");
        var tiny = h.Refine(h.Int, ComparisonKind.LessThan, 5L, "Tiny");
        var either = new UnionType([big, small]);

        Assert.True(ConversionRelation.IsWithin(either, new UnionType([big, small, h.String])));
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(either, new UnionType([h.String, small, big]), h.Built.Interner));
        Assert.False(ConversionRelation.IsWithin(either, new UnionType([small, h.String])));
        Assert.False(ConversionRelation.IsWithin(new UnionType([tiny, big]), either));
    }
}