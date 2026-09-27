// Tier 2 (Functional): the purity, readonly, conformance, ownership, pattern and assignability rules judge hand-built purity fragments.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PurityRefusalTests
{
    [Theory]
    [InlineData("abstract-auto-getter-answered-by-an-impure-implementation", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("abstract-default-body-replaced-by-an-impure-implementation", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("abstract-pure-indexer-implemented-impurely", "MIST_PA_PURITY_CONTRACT_NOT_MET")]
    [InlineData("clr-delegate-to-pure-func", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("constructor-assigns-another-instances-readonly-field", "MIST_PA_READONLY_FIELD_ASSIGNED")]
    [InlineData("constructor-of-another-type-assigns-a-readonly-field", "MIST_PA_READONLY_FIELD_ASSIGNED")]
    [InlineData("default-entry-not-consistent", "MIST_PA_DEFAULT_NOT_CONSISTENT")]
    [InlineData("default-initializer-not-consistent", "MIST_PA_DEFAULT_NOT_CONSISTENT")]
    [InlineData("frozen-method-group-to-pure-func", "MIST_PA_PURE_FUNCTION_REQUIRED")]
    [InlineData("generic-type-test-instantiated-with-a-pure-function-type", "MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE")]
    [InlineData("implicit-clone-of-a-frozen-input-through-an-impure-clone", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("impure-method-group-to-pure-func", "MIST_PA_PURE_FUNCTION_REQUIRED")]
    [InlineData("local-abstract-member-implemented-impurely", "MIST_PA_PURITY_CONTRACT_NOT_MET")]
    [InlineData("local-method-reads-through-an-argument", "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT")]
    [InlineData("local-readonly-method-writes-its-instance", "MIST_PA_PURE_SCOPE_WRITES")]
    [InlineData("local-type-member-reads-an-argument", "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT")]
    [InlineData("owned-return-of-this", "MIST_PA_OWNED_RETURN_NOT_OWNED")]
    [InlineData("owned-slot-answered-by-an-unowned-return", "MIST_PA_SLOT_RETURN_NOT_OWNED")]
    [InlineData("plain-func-to-pure-func", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("pure-abstract-member-implemented-impurely", "MIST_PA_PURITY_CONTRACT_NOT_MET")]
    [InlineData("pure-constructor-chains-to-an-unmarked-one", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-function-type-tested-at-runtime", "MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE")]
    [InlineData("pure-lambda-writes-a-captured-local", "MIST_PA_PURE_SCOPE_WRITES")]
    [InlineData("pure-method-allocates-through-an-implicit-constructor-with-an-impure-initializer", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-awaits-a-task", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-calls-an-impure-conversion", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-calls-an-impure-operator", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-calls-an-unmarked-method", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-calls-console", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-holds-a-linear-with-an-impure-drop", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-invokes-a-plain-function-value", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-iterates-an-impure-collection", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-launders-a-write-through-nested-lambdas", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-reads-a-mutable-field", "MIST_PA_PURE_SCOPE_READS_MUTABLE")]
    [InlineData("pure-method-reads-a-span-parameter", "MIST_PA_PURE_SCOPE_READS_MUTABLE")]
    [InlineData("pure-method-reads-an-impure-indexer", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-reads-the-count-of-an-unrestricted-list", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-runs-an-async-block", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("pure-method-stores-a-frozen-reference", "MIST_PA_PURE_SCOPE_STORES_UNOWNED")]
    [InlineData("pure-method-writes-a-span-parameter", "MIST_PA_PURE_SCOPE_WRITES")]
    [InlineData("pure-method-writes-a-static", "MIST_PA_PURE_SCOPE_WRITES")]
    [InlineData("pure-method-writes-an-element-of-a-frozen-list", "MIST_PA_PURE_SCOPE_WRITES")]
    [InlineData("pure-type-member-writes", "MIST_PA_PURE_SCOPE_WRITES")]
    [InlineData("readonly-field-passed-by-ref", "MIST_PA_READONLY_FIELD_ASSIGNED")]
    [InlineData("readonly-method-calls-a-mutable-conversion-operator", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-iterates-an-impure-collection", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-reads-a-mutable-getter-in-a-property-pattern", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-runs-a-mutable-setter-through-an-object-initializer", "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY")]
    [InlineData("readonly-method-writes-an-element-of-an-array-parameter", "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX")]
    [InlineData("readonly-method-writes-through-a-ref-parameter", "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_REF")]
    [InlineData("readonly-type-settable-property-read-through-an-unrestricted-reference", "MIST_PA_PURE_SCOPE_CALLS_IMPURE")]
    [InlineData("static-initializer-lambda-converting-to-a-pure-function-type", "MIST_PA_PURE_SCOPE_WRITES")]
    [InlineData("struct-local-member-writes-through-an-owned-field-of-a-copy", "MIST_PA_PURE_SCOPE_WRITES")]
    public void RefusedFragmentKeepsItsDiagnostic(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new PurityFragments().Analyze(caseName));

        Assert.True(diagnostic.Id.Value == expected, $"{caseName}: expected {expected}, got {diagnostic.Id.Value}");
    }
}