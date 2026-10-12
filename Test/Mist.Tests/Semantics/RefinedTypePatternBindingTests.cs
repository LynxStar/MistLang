using Mist.Generated;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 2: PatternBinder's type-test chain over hand-built syntax, asserting the predicate the test carries.
[Trait("Tier", "2")]
public sealed class RefinedTypePatternBindingTests
{
    [Theory]
    [InlineData(-1L, false)]
    [InlineData(1L, true)]
    [InlineData(10L, false)]
    public void ATypeTestOfARefinementOverARefinement_CarriesEveryPredicate(long value, bool admitted)
    {
        var f = new BindingFragments();
        var integer = f.Built.Type("Int32");
        var positive = Refine(integer, ComparisonKind.GreaterThan, 0L, "Positive");
        var smallPositive = Refine(positive, ComparisonKind.LessThan, 10L, "SmallPositive");
        f.Register(smallPositive);

        var bound = Assert.IsType<TypeTestPattern>(PatternBinder.Bind(BareType("SmallPositive"), integer, f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(smallPositive, bound.NarrowedType);
        Assert.Equal(new[] { positive.Predicate!, smallPositive.Predicate! }, Assert.IsType<ConjunctionPattern>(bound.Refinement).Operands);
        Assert.Equal(admitted, Requirement.Of(bound)!.Values.Contains(value));
    }

    [Fact]
    public void ATypeTestOfARefinedConstituent_GoesThroughThatConstituent()
    {
        var f = new BindingFragments();
        var integer = f.Built.Type("Int32");
        var small = Refine(integer, ComparisonKind.LessThan, 10L, "Small");
        var big = Refine(integer, ComparisonKind.GreaterThan, 100L, "Big");
        f.Register(small);
        f.Register(big);

        var bound = Assert.IsType<TypeTestPattern>(PatternBinder.Bind(BareType("Big"), new UnionType([big, small]), f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(big, bound.Constituent);
        Assert.Same(big.Predicate, bound.Refinement);
    }

    private static RefinedType Refine(IMistType type, ComparisonKind kind, long bound, string name)
    {
        var integer = Requirement.Unrefined(type);
        return HandBuiltRefinements.RefinePattern(type, new ComparisonPattern(HandBuiltModel.Node(), type, kind, HandBuiltModel.Literal(integer, bound)), name);
    }

    private static PatternNode BareType(string name)
    {
        var primary = BindingFragments.At<BareTypePatternPrimaryPatternNode>();
        primary.Type = BindingFragments.TypeName(name);
        return new()
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };
    }
}