using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class RefinementCompositionTests
{
    private readonly HandBuiltRefinements h = new();

    [Fact]
    public void AnUnrefinedType_HasNoPredicateToTest()
    {
        Assert.Null(Compose(h.Int));
        Assert.Null(Compose(AliasOf("Count", h.Int)));
    }

    [Fact]
    public void ARefinementOverItsBase_TestsItsOwnPredicateAlone()
    {
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");

        Assert.Same(positive.Predicate, Compose(positive));
    }

    [Theory]
    [InlineData(-1L, false)]
    [InlineData(0L, false)]
    [InlineData(1L, true)]
    [InlineData(9L, true)]
    [InlineData(10L, false)]
    public void ARefinementOverARefinement_TestsEveryPredicateInTheChain(long value, bool admitted)
    {
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var small = h.Refine(positive, ComparisonKind.LessThan, 10L, "SmallPositive");

        var composed = Assert.IsType<ConjunctionPattern>(Compose(small));

        Assert.Same(small, composed.NarrowedType);
        Assert.Equal(new[] { positive.Predicate!, small.Predicate! }, composed.Operands);
        Assert.Equal(admitted, Requirement.Of(composed)!.Values.Contains(value));
    }

    [Fact]
    public void AnAliasBetweenTwoRefinements_IsSeenThrough()
    {
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var narrow = h.Refine(AliasOf("Wrapped", positive), ComparisonKind.LessThan, 5L, "Narrow");

        var composed = Assert.IsType<ConjunctionPattern>(Compose(narrow));

        Assert.Equal(new[] { positive.Predicate!, narrow.Predicate! }, composed.Operands);
    }

    [Theory]
    [InlineData(5L, true)]
    [InlineData(50L, false)]
    [InlineData(95L, true)]
    [InlineData(150L, false)]
    public void ConjunctionsFlatten_WhileADisjunctionStaysOneOperand(long value, bool admitted)
    {
        var range = h.Between(h.Int, 0L, 100L, "Percent");
        var either = new DisjunctionPattern(HandBuiltModel.Node(), range,
            [h.Compare(range, ComparisonKind.LessThan, 10L), h.Compare(range, ComparisonKind.GreaterThan, 90L)]);
        var edges = HandBuiltRefinements.RefinePattern(range, either, "Edges");
        var three = h.Refine(edges, ComparisonKind.Equal, 3L, "Three");

        var composed = Assert.IsType<ConjunctionPattern>(Compose(edges));

        Assert.Equal(((ConjunctionPattern)range.Predicate!).Operands.Append(either), composed.Operands);
        Assert.Equal(4, Assert.IsType<ConjunctionPattern>(Compose(three)).Operands.Count);
        Assert.Equal(admitted, Requirement.Of(composed)!.Values.Contains(value));
    }

    [Fact]
    public void AnUnboundPredicateAnywhereInTheChain_LeavesNothingToTest()
    {
        var unbound = HandBuiltModel.Refined(h.Int, "Unbound");
        var above = h.Refine(unbound, ComparisonKind.LessThan, 10L, "Above");

        Assert.Null(Compose(unbound));
        Assert.Null(Compose(above));
    }

    private static Pattern? Compose(IMistType tested)
    {
        return PatternBinder.ComposeRefinement(tested, HandBuiltModel.Node(), refined => refined.Predicate);
    }
}