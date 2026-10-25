// Tier 2 (Functional): the promises a scope reads off something other than the body that keeps them -- a
// struct copy's owned reference field, an abstract accessor or default body, an owned slot, an erased
// type test, a readonly type's settable property, an initializer lambda, a params element, an implicit
// clone and an implicit constructor -- each pinned by a hand-built fragment where the promise is judged.
// The refused half of several of these rows is a row of PurityRefusalTests.

using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PuritySoundnessRoundTwoTests
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string ContractNotMet = "MIST_PA_PURITY_CONTRACT_NOT_MET";
    private const string Untestable = "MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE";
    private const string CallsMutable = "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE";

    [Fact]
    public void AStructsLocalReadonlyMember_MayNotReadThroughAnOwnedReferenceFieldOfTheCopy()
    {
        var finding = Assert.Single(Analyze("struct-local-readonly-member-reads-through-an-owned-field-of-a-copy"));

        Assert.Equal(ReadsMutable, finding.Number.Value);
        Assert.Contains("'Look'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure-method-calls-an-unmarked-struct-member-writing-through-an-owned-field", CallsImpure)]
    [InlineData("readonly-method-calls-an-unmarked-struct-member-writing-through-an-owned-field", CallsMutable)]
    public void AnUnmarkedStructMemberWritingThroughAnOwnedReferenceField_IsNotLocalToTheHolderOfACopy(string fragment, string expected)
    {
        var finding = Assert.Single(Analyze(fragment));

        Assert.Equal(expected, finding.Number.Value);
        Assert.Contains("Bump", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("struct-local-member-writes-the-copys-own-value-field")]
    [InlineData("struct-local-readonly-member-reads-the-copys-own-value-field")]
    [InlineData("pure-method-calls-a-structs-local-members-on-a-parameter")]
    public void AStructsLocalMember_MayStillWriteAndReadTheCopysOwnValueFields(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Fact]
    public void AnAbstractAutoGetter_KeepsOnlyWhatEveryImplementationKeeps()
    {
        var finding = Assert.Single(Analyze("readonly-method-reads-an-abstract-auto-getter-answered-by-an-impure-implementation"));

        Assert.Equal(CallsMutable, finding.Number.Value);
        Assert.Contains("Area", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbstractAutoSetter_KeepsOnlyWhatEveryImplementationKeeps()
    {
        var finding = Assert.Single(Analyze("pure-method-sets-an-abstract-auto-setter-answered-by-an-impure-implementation"));

        Assert.Equal(CallsImpure, finding.Number.Value);
        Assert.Contains("'Fill'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbstractAutoGetter_EveryImplementationOfWhichIsPure_StaysPure()
    {
        Assert.Empty(Analyze("pure-method-reads-an-abstract-auto-getter-answered-by-pure-implementations"));
    }

    [Theory]
    [InlineData("pure-method-calls-an-abstract-default-body-no-conformer-replaces")]
    [InlineData("pure-method-calls-an-abstract-default-body-replaced-by-a-pure-implementation")]
    public void AnAbstractDefaultBody_KeptByEveryConformer_StaysPure(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Theory]
    [InlineData("owned-slot-answered-by-an-owned-return")]
    [InlineData("pure-method-writes-the-owned-result-of-an-owned-slot")]
    public void AnOwnedSlotAnsweredByAnOwnedReturn_IsOwnedByAPureCaller(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Fact]
    public void AGenericTypeTest_InstantiatedWithAPureFunctionType_IsUntestable()
    {
        var finding = Assert.Single(Analyze("generic-as-instantiated-with-a-pure-function-type"));

        Assert.Equal(Untestable, finding.Number.Value);
        Assert.Contains("pure func<", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("generic-type-test-instantiated-with-int")]
    [InlineData("generic-as-instantiated-with-string")]
    public void AGenericTypeTest_InstantiatedWithoutAPureFunctionType_IsAdmitted(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Fact]
    public void AReadonlyTypesSettablePropertyStorage_IsNotStable()
    {
        var finding = Assert.Single(Analyze("readonly-type-field-backed-settable-property-read-through-an-unrestricted-reference"));

        Assert.Equal(CallsImpure, finding.Number.Value);
        Assert.Contains("'Peek'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("readonly-type-settable-property-read-through-a-frozen-reference")]
    [InlineData("readonly-type-init-only-property-read-through-an-unrestricted-reference")]
    public void AReadonlyTypesProperty_IsReadableWhereItsStorageCannotChangeUnderTheReader(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Theory]
    [InlineData("static-property-initializer-lambda-converting-to-a-pure-function-type")]
    [InlineData("instance-field-initializer-lambda-converting-to-a-pure-function-type")]
    public void AnInitializerThatIsALambda_IsJudgedWhereItConvertsToAPureFunctionType(string fragment)
    {
        var finding = Assert.Single(Analyze(fragment));

        Assert.Equal(Writes, finding.Number.Value);
        Assert.Contains("lambda", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("static-field-initializer-pure-lambda")]
    [InlineData("instance-field-initializer-pure-lambda")]
    [InlineData("settable-property-initializer-pure-lambda")]
    public void AnInitializerThatIsAPureLambda_IsAdmittedWithoutFaultingTheInference(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Fact]
    public void ALambdaPassedThroughAParamsParameter_IsJudgedAgainstTheElementType()
    {
        var finding = Assert.Single(Analyze("pure-lambda-argument-to-a-params-parameter-writes-a-static"));

        Assert.Equal(Writes, finding.Number.Value);
        Assert.Contains("lambda", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PureLambdasPassedThroughAParamsParameter_AreAdmitted()
    {
        Assert.Empty(Analyze("pure-lambda-arguments-to-a-params-parameter"));
    }

    [Fact]
    public void APureMethod_MayIterateItsOwnParamsArray()
    {
        Assert.Empty(Analyze("pure-method-iterates-its-own-params-array"));
    }

    [Fact]
    public void APureMethod_StillMayNotIterateAnOrdinaryArrayParameter()
    {
        var finding = Assert.Single(Analyze("pure-method-iterates-an-ordinary-array-parameter"));

        Assert.Equal(ReadsMutable, finding.Number.Value);
        Assert.Contains("'Sum'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImplicitCloneOfAnExternalObject_ReadsItsCurrentState()
    {
        var findings = Analyze("implicit-clone-of-an-external-object");

        Assert.NotEmpty(findings);
        Assert.All(findings, finding => Assert.Contains(finding.Number.Value, new[] { CallsImpure, ReadsMutable }));
    }

    [Fact]
    public void AnImplicitCloneOfAFrozenInputThroughACloneThatReadsOnlyItsSource_IsAdmitted()
    {
        Assert.Empty(Analyze("implicit-clone-of-a-frozen-input-through-a-clone-reading-only-its-source"));
    }

    [Fact]
    public void AnAbstractIndexersPromise_BindsEveryImplementation()
    {
        var finding = Assert.Single(Analyze("abstract-local-indexer-implemented-with-an-impure-setter"));

        Assert.Equal(ContractNotMet, finding.Number.Value);
        Assert.Contains("Grid", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abstract-pure-indexer-implemented-purely")]
    [InlineData("abstract-local-indexer-implemented-locally")]
    public void AnAbstractIndexersPromise_KeptByTheImplementation_IsAdmitted(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    [Fact]
    public void AnImplicitConstructorWhoseInitializerReadsAStatic_IsImpureToItsCaller()
    {
        var finding = Assert.Single(Analyze("pure-caller-of-an-unmarked-method-allocating-through-an-implicit-constructor-with-an-impure-initializer"));

        Assert.Contains(finding.Number.Value, new[] { CallsImpure, ReadsMutable });
        Assert.Contains("'Use'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure-method-allocates-through-an-implicit-constructor-with-pure-initializers")]
    [InlineData("pure-caller-of-an-unmarked-method-allocating-through-an-implicit-constructor-with-pure-initializers")]
    public void AnImplicitConstructorWhoseInitializersArePure_KeepsTheAllocatingMemberPure(string fragment)
    {
        Assert.Empty(Analyze(fragment));
    }

    private static IReadOnlyList<Diagnostic> Analyze(string fragment)
    {
        return new PuritySoundnessFragments().Analyze(fragment);
    }
}