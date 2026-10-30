// Tier 2 (Functional): purity Requirement 1.5 at the judge -- an accessor's own promise replaces its declaration's wherever a promise is read, and is what its body is held to. The purity and readonly rules own every id these rows name, so each fragment is judged by both and an admitted row is silent under both.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AccessorPromiseRefusalTests
{
    private const string Writes = "MIST0137";
    private const string ReadsMutable = "MIST0138";
    private const string CallsImpure = "MIST0139";
    private const string SetsProperty = "MIST0259";

    [Theory]
    [InlineData("pure-method-reads-a-local-readonly-indexer-of-a-frozen-input", null)]
    [InlineData("pure-method-sets-a-local-indexer-of-a-frozen-input", Writes)]
    [InlineData("pure-method-sets-and-reads-a-local-indexer-it-owns", null)]
    [InlineData("pure-method-reads-a-local-readonly-indexer-through-an-unrestricted-input", CallsImpure)]
    [InlineData("pure-method-reads-a-writing-getter-of-a-frozen-input", CallsImpure)]
    [InlineData("pure-method-reads-a-writing-getter-it-owns", null)]
    [InlineData("pure-method-reads-a-local-readonly-getter-of-a-frozen-input", null)]
    [InlineData("pure-method-reads-a-local-readonly-getter-through-an-unrestricted-input", CallsImpure)]
    [InlineData("pure-method-sets-a-local-setter-it-owns", null)]
    [InlineData("pure-method-sets-a-local-setter-of-a-frozen-input", Writes)]
    [InlineData("readonly-method-reads-a-local-readonly-getter-through-an-unrestricted-input", null)]
    [InlineData("readonly-method-sets-a-local-setter-through-an-unrestricted-input", SetsProperty)]
    [InlineData("readonly-method-sets-a-local-setter-it-owns", null)]
    public void AnAccessorsOwnPromise_IsWhatACallerReads(string caseName, string? expected)
    {
        var reported = new AccessorPromiseFragments().Analyze(caseName);

        if(expected is null)
        {
            Assert.Empty(reported);
            return;
        }

        var finding = Assert.Single(reported);
        Assert.True(finding.Number.Value == expected, $"{caseName}: expected {expected}, got {finding.Number.Value}");
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure-getter-reads-a-mutable-field", ReadsMutable, "pure property 'P'")]
    [InlineData("pure-getter-reads-a-static", ReadsMutable, "pure property 'P'")]
    [InlineData("local-readonly-getter-writes-its-instance", Writes, "local readonly property 'P'")]
    [InlineData("local-readonly-getter-of-a-local-property-writes-its-instance", Writes, "local readonly property 'P'")]
    [InlineData("local-readonly-indexer-getter-writes-its-instance", Writes, "local readonly indexer")]
    [InlineData("local-setter-writes-a-static", Writes, "local property 'P'")]
    public void AnAccessorsBody_IsHeldToItsOwnPromise(string caseName, string expected, string scope)
    {
        var finding = Assert.Single(new AccessorPromiseFragments().Analyze(caseName));

        Assert.True(finding.Number.Value == expected, $"{caseName}: expected {expected}, got {finding.Number.Value}");
        Assert.Contains(scope, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("local-readonly-getter-and-local-setter-keep-their-promises")]
    [InlineData("local-property-with-a-local-readonly-getter-keeps-its-promise")]
    [InlineData("pure-getter-reads-a-readonly-field-its-pure-constructor-set")]
    [InlineData("local-readonly-indexer-getter-and-local-setter-keep-their-promises")]
    public void AnAccessorsBody_KeepingItsOwnPromise_IsAdmitted(string caseName)
    {
        Assert.Empty(new AccessorPromiseFragments().Analyze(caseName));
    }
}