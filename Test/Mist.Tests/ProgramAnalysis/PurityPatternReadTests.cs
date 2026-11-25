// What a pure scope reads through a pattern, a counted slice and an index, and what each refusal names
// (specs/purity/requirements.md Requirements 7, 8.1 and 10; the judge table in specs/purity/design.md).
// The walk cases are Tier 2 over hand-built bodies; whether a type formats by type is one Tier 1 decision.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class PurityPatternReadTests
{
    private const string ReadsMutable = "MIST0138";
    private const string CallsImpure = "MIST0139";

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("counted-list-pattern-slices-through-an-impure-slice-method", "call 'Slice'")]
    [InlineData("counted-list-pattern-slices-through-an-impure-range-indexer", "read 'this[]'")]
    public void ACountedListPatternJudgesItsSliceAgainstTheScrutinee(string fragment, string named)
    {
        Refused(fragment, CallsImpure, named);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ACountedListPatternWhoseMembersArePureIsAdmitted()
    {
        Assert.Empty(new PurityFragments().PatternReads("counted-list-pattern-slices-through-pure-members"));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ACountRefusedForTheListPatternIsNotRefusedAgainForItsSlice()
    {
        Refused("counted-list-pattern-with-an-impure-count-slices-through-an-impure-slice-method", CallsImpure, "read 'Count'");
    }

    [Fact]
    [Trait("Tier", "2")]
    public void EveryOperandOfADisjunctionIsJudged()
    {
        Refused("disjunction-reads-a-mutable-field-in-its-second-operand", ReadsMutable, "field 'Level'");
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ASubpatternWhoseMemberTheBinderRefusedReadsNothing()
    {
        Assert.Empty(new PurityFragments().PatternReads("property-pattern-names-a-static-field-the-binder-refused"));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("pure-getter-reads-the-backing-field-of-a-get-only-property", null)]
    [InlineData("pure-getter-reads-the-backing-field-of-an-init-only-property", null)]
    [InlineData("pure-getter-reads-the-backing-field-of-a-settable-property", ReadsMutable)]
    public void TheBackingFieldOfAPropertyWithNoSetterOrAnInitIsStable(string fragment, string? expected)
    {
        var findings = new PurityFragments().PatternReads(fragment);

        Assert.Equal(expected, Assert.Single(findings.DefaultIfEmpty())?.Number.Value);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AnElementOfAReadonlyValueHeldInAStableFieldIsAdmitted()
    {
        Assert.Empty(new PurityFragments().PatternReads("pure-method-indexes-a-readonly-value-held-in-a-stable-field"));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("allocation", "call the constructor of 'Noisy'")]
    [InlineData("bare-static-field", "read 'Seed'")]
    [InlineData("bare-static-property", "read 'Level'")]
    [InlineData("unary-operator", "call operator -")]
    [InlineData("explicit-conversion", "call the conversion to 'int'")]
    [InlineData("binary-operator", "call operator +")]
    [InlineData("implicit-conversion", "call the conversion to 'int'")]
    [InlineData("enum-lookup-cast", "convert to 'Noisy'")]
    public void ARefusedStaticInitializerNamesTheOperationThatRunsIt(string trigger, string operation)
    {
        var finding = Assert.Single(new PurityFragments().NoisyTriggers(trigger));

        Assert.Equal(CallsImpure, finding.Number.Value);
        Assert.Contains($"{operation}, which runs the static initializers of 'Noisy',", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("named-tuple-of-an-int-and-a-string", true)]
    [InlineData("named-tuple-of-an-int-and-a-clr-type", false)]
    [InlineData("alias-of-an-enum", true)]
    [InlineData("alias-of-a-clr-type", false)]
    [InlineData("enum", true)]
    [InlineData("newtype", true)]
    [InlineData("function-type", true)]
    public void ATypeFormatsByTypeWhenWhatItHoldsDoes(string shape, bool expected)
    {
        Assert.Equal(expected, new PurityFragments().FormatsByType(shape));
    }

    private static void Refused(string fragment, string expected, string named)
    {
        var finding = Assert.Single(new PurityFragments().PatternReads(fragment));

        Assert.True(finding.Number.Value == expected, $"{fragment}: expected {expected}, got {finding.Number.Value}: {finding.Message}");
        Assert.Contains(named, finding.Message, StringComparison.Ordinal);
    }
}