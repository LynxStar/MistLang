// Tier 2 (Functional): Requirement 8's inference over hand-built members calling each other, observed
// through a DECLARED pure caller, since an inferred promise is never reported on the member that keeps
// it (Requirement 3). Every callee declares nothing; what it keeps is read off its body, its
// implementations, or its storage, as specs/purity/design.md's PurityInference section states.

using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class PurityInferenceTests
{
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";

    [Theory]
    [InlineData("helper-reading-a-readonly-static")]
    [InlineData("helper-writing-an-array-it-allocated")]
    [InlineData("helper-calling-a-pure-helper")]
    public void AHelperWhoseBodyKeepsPurity_IsCallableFromADeclaredPureMember(string helper)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(helper));
    }

    [Theory]
    [InlineData("helper-reading-a-mutable-static")]
    [InlineData("helper-writing-a-static")]
    [InlineData("helper-calling-an-impure-helper")]
    public void AHelperWhoseBodyBreaksPurity_IsRefusedAtTheCallerNamingIt(string helper)
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze(helper));

        Assert.Equal(CallsImpure, finding.Number.Value);
        Assert.Contains("Helper", finding.Message, StringComparison.Ordinal);
        Assert.Contains("'Caller'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MutuallyRecursiveUndeclaredMembers_KeepPurityAsAGreatestFixpoint()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("mutually-recursive-helpers"));
    }

    [Fact]
    public void ASelfRecursiveUndeclaredMember_KeepsPurity()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("self-recursive-helper"));
    }

    [Fact]
    public void AnAbstractMemberWithAnImpureImplementation_IsRefusedThroughTheAbstract()
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze("abstract-member-with-an-impure-implementation"));

        Assert.Equal(CallsImpure, finding.Number.Value);
        Assert.Contains("Area", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbstractMemberWhoseEveryImplementationIsPure_IsAdmittedThroughTheAbstract()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("abstract-member-whose-every-implementation-is-pure"));
    }

    [Fact]
    public void AnAbstractMemberWithNoImplementationInTheProgram_KeepsEveryPromise()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("abstract-member-with-no-implementation"));
    }

    [Fact]
    public void AnAbstractMemberDeclaredPure_IsAdmittedWhateverItsImplementationsDeclare()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("abstract-member-declared-pure"));
    }

    [Theory]
    [InlineData("auto-getter-over-stable-storage-read-through-an-external-receiver")]
    [InlineData("auto-getter-over-mutable-storage-read-through-a-frozen-input")]
    [InlineData("auto-getter-over-mutable-storage-read-through-a-fresh-local")]
    [InlineData("auto-getter-over-mutable-storage-set-and-read-through-an-owned-local")]
    public void AnAutoGetter_IsPureOverStableStorageAndLocalReadonlyOverMutable(string caller)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(caller));
    }

    [Fact]
    public void AnAutoGetterOverMutableStorage_IsRefusedOnAnExternalReceiver()
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze("auto-getter-over-mutable-storage-read-through-an-external-receiver"));

        Assert.Equal(CallsImpure, finding.Number.Value);
        Assert.Contains("Level", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGetterWithABodyOverStableFields_IsInferredPure()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("getter-with-a-body-over-stable-fields"));
    }

    [Fact]
    public void AnUndeclaredConstructorThatOnlyAssignsItsOwnFields_IsInferredPure()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("undeclared-constructor-assigning-its-own-fields"));
    }

    [Fact]
    public void AnUndeclaredConstructorThatWritesAStatic_IsRefusedAtTheAllocation()
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze("undeclared-constructor-writing-a-static"));

        Assert.Equal(CallsImpure, finding.Number.Value);
    }
}