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