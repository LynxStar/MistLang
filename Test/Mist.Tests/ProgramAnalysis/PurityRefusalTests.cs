// Tier 2 (Functional): the purity, readonly, conformance, ownership, pattern and assignability rules judge hand-built purity fragments.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PurityRefusalTests
{
    [Theory]
    [InlineData("abstract-auto-getter-answered-by-an-impure-implementation", "MIST0139")]
    [InlineData("abstract-default-body-replaced-by-an-impure-implementation", "MIST0139")]
    [InlineData("abstract-pure-indexer-implemented-impurely", "MIST0144")]
    [InlineData("clr-delegate-to-pure-func", "MIST0157")]
    [InlineData("constructor-assigns-another-instances-readonly-field", "MIST0262")]
    [InlineData("constructor-of-another-type-assigns-a-readonly-field", "MIST0262")]
    [InlineData("default-entry-not-consistent", "MIST0143")]
    [InlineData("default-initializer-not-consistent", "MIST0143")]
    [InlineData("frozen-method-group-to-pure-func", "MIST0142")]
    [InlineData("generic-type-test-instantiated-with-a-pure-function-type", "MIST0146")]
    [InlineData("implicit-clone-of-a-frozen-input-through-an-impure-clone", "MIST0139")]
    [InlineData("impure-method-group-to-pure-func", "MIST0142")]
    [InlineData("local-abstract-member-implemented-impurely", "MIST0144")]
    [InlineData("local-method-reads-through-an-argument", "MIST0140")]
    [InlineData("local-readonly-method-writes-its-instance", "MIST0137")]
    [InlineData("local-type-member-reads-an-argument", "MIST0140")]
    [InlineData("owned-return-of-this", "MIST0263")]
    [InlineData("owned-slot-answered-by-an-unowned-return", "MIST0186")]
    [InlineData("plain-func-to-pure-func", "MIST0157")]
    [InlineData("pure-abstract-member-implemented-impurely", "MIST0144")]
    [InlineData("pure-constructor-chains-to-an-unmarked-one", "MIST0139")]
    [InlineData("pure-function-type-tested-at-runtime", "MIST0145")]
    [InlineData("pure-lambda-writes-a-captured-local", "MIST0137")]
    [InlineData("pure-method-allocates-through-an-implicit-constructor-with-an-impure-initializer", "MIST0139")]
    [InlineData("pure-method-awaits-a-task", "MIST0139")]
    [InlineData("pure-method-calls-an-impure-conversion", "MIST0139")]
    [InlineData("pure-method-calls-an-impure-operator", "MIST0139")]
    [InlineData("pure-method-calls-an-unmarked-method", "MIST0139")]
    [InlineData("pure-method-calls-console", "MIST0139")]
    [InlineData("pure-method-holds-a-linear-with-an-impure-drop", "MIST0139")]
    [InlineData("pure-method-invokes-a-plain-function-value", "MIST0139")]
    [InlineData("pure-method-iterates-an-impure-collection", "MIST0139")]
    [InlineData("pure-method-launders-a-write-through-nested-lambdas", "MIST0139")]
    [InlineData("pure-method-reads-a-mutable-field", "MIST0138")]
    [InlineData("pure-method-reads-a-span-parameter", "MIST0138")]
    [InlineData("pure-method-reads-an-impure-indexer", "MIST0139")]
    [InlineData("pure-method-reads-the-count-of-an-unrestricted-list", "MIST0139")]
    [InlineData("pure-method-runs-an-async-block", "MIST0139")]
    [InlineData("pure-method-stores-a-frozen-reference", "MIST0141")]
    [InlineData("pure-method-writes-a-span-parameter", "MIST0137")]
    [InlineData("pure-method-writes-a-static", "MIST0137")]
    [InlineData("pure-method-writes-an-element-of-a-frozen-list", "MIST0137")]
    [InlineData("pure-type-member-writes", "MIST0137")]
    [InlineData("readonly-field-passed-by-ref", "MIST0262")]
    [InlineData("readonly-method-calls-a-mutable-conversion-operator", "MIST0257")]
    [InlineData("readonly-method-iterates-an-impure-collection", "MIST0257")]
    [InlineData("readonly-method-reads-a-mutable-getter-in-a-property-pattern", "MIST0257")]
    [InlineData("readonly-method-runs-a-mutable-setter-through-an-object-initializer", "MIST0259")]
    [InlineData("readonly-method-writes-an-element-of-an-array-parameter", "MIST0256")]
    [InlineData("readonly-method-writes-through-a-ref-parameter", "MIST0261")]
    [InlineData("readonly-type-settable-property-read-through-an-unrestricted-reference", "MIST0139")]
    [InlineData("static-initializer-lambda-converting-to-a-pure-function-type", "MIST0137")]
    [InlineData("struct-local-member-writes-through-an-owned-field-of-a-copy", "MIST0137")]
    public void RefusedFragmentKeepsItsDiagnostic(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new PurityFragments().Analyze(caseName));

        Assert.True(diagnostic.Number.Value == expected, $"{caseName}: expected {expected}, got {diagnostic.Number.Value}");
    }
}