// Tier 2 (Functional): purity Requirement 4.1 read against the readonly analyzer over hand-built contexts --
// a readonly member writes nothing External however the write is spelled, while what it OWNS stays
// writable and a readonly field is assignable through this in a constructor of its own type. The refused
// spellings -- a ref parameter, an array parameter's element, an initializer's setter, a property pattern's
// getter, an explicit conversion, another instance's readonly field, a readonly field passed by ref -- are
// rows of PurityRefusalTests; the ids are the readonly analyzer's own.

using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReadonlyContextGapTests
{
    private const string CallsMutable = "MIST0257";
    private const string SetsProperty = "MIST0259";
    private const string WritesThroughRef = "MIST0261";

    [Fact]
    public void AReadonlyMember_MayNotWriteThroughARef()
    {
        var finding = Assert.Single(Analyze("readonly-method-writes-through-a-ref-local-aliasing-a-field"));

        Assert.Equal(WritesThroughRef, finding.Number.Value);
    }

    [Fact]
    public void AReadonlyMember_MayStillFillAnArrayItAllocated()
    {
        Assert.Empty(Analyze("readonly-method-fills-an-array-it-allocated"));
    }

    [Theory]
    [InlineData("readonly-method-runs-an-auto-setter-through-an-object-initializer")]
    [InlineData("readonly-method-sets-an-auto-property-on-what-it-owns")]
    public void AReadonlyMember_MayStillCallAnAutoSetterOnWhatItOwns(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Theory]
    [InlineData("readonly-method-sets-a-property-whose-setter-writes-a-static-on-what-it-owns")]
    [InlineData("readonly-method-sets-an-indexer-whose-setter-writes-a-static-on-what-it-owns")]
    public void AReadonlyMember_MayNotCallASideEffectingSetterOnWhatItOwns(string fragment)
    {
        Assert.Equal(SetsProperty, Assert.Single(Analyze(fragment)).Number.Value);
    }

    [Fact]
    public void AReadonlyMember_MayMatchAnAutoPropertyInAPattern()
    {
        Assert.Empty(Analyze("readonly-method-matches-an-auto-property-in-a-pattern"));
    }

    [Fact]
    public void AReadonlyMember_MayNotReadASideEffectingExtensionGetter()
    {
        var finding = Assert.Single(Analyze("readonly-method-reads-an-extension-getter-that-writes-a-static"));

        Assert.Equal(CallsMutable, finding.Number.Value);
        Assert.Contains("Ext", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMember_MayReadAnExtensionGetterThatOnlyReads()
    {
        Assert.Empty(Analyze("readonly-method-reads-an-extension-getter-that-only-reads"));
    }

    [Fact]
    public void AReadonlyMember_MayNotCallASideEffectingConversion()
    {
        var finding = Assert.Single(Analyze("readonly-method-calls-an-implicit-conversion-that-writes-a-static"));

        Assert.Equal(CallsMutable, finding.Number.Value);
    }

    [Theory]
    [InlineData("readonly-method-calls-an-explicit-conversion-that-writes-nothing")]
    [InlineData("readonly-method-calls-an-implicit-conversion-that-writes-nothing")]
    public void AReadonlyMember_MayCallAConversionThatWritesNothing(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Fact]
    public void AReadonlyMember_MayNotCloneThroughASideEffectingClone()
    {
        var finding = Assert.Single(Analyze("readonly-method-clones-through-a-clone-that-writes-a-static"));

        Assert.Equal(CallsMutable, finding.Number.Value);
        Assert.Contains("Clone", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMember_MayCloneThroughACloneThatReadsOnlyItsSource()
    {
        Assert.Empty(Analyze("readonly-method-clones-through-a-clone-that-reads-only-its-source"));
    }

    [Theory]
    [InlineData("constructor-assigns-its-own-readonly-field")]
    [InlineData("constructor-assigns-its-own-readonly-field-through-this")]
    public void AReadonlyField_IsAssignableThroughThisInItsOwnConstructor(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    private static IReadOnlyList<Diagnostic> Analyze(string fragment)
    {
        return new ReadonlyContextFragments().Analyze(fragment);
    }
}