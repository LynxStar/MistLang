// Tier 2 (Functional): purity Requirement 11, the fifteenth readonly-contexts rule, over hand-built contexts --
// a readonly context may write a field, an index or a setter of what it OWNS and may call a local member
// on it, while a write through an unrestricted parameter and a call to an unmarked member stay refused.
// The ids are the readonly analyzer's own, since the amendment changes what it admits, not what it says.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReadonlyOwnedWritesTests
{
    private const string FieldAssigned = "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER";
    private const string CallsMutable = "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE";

    [Theory]
    [InlineData("readonly-method-writes-and-locally-calls-an-owned-local")]
    [InlineData("readonly-method-writes-and-locally-calls-a-fresh-local")]
    [InlineData("readonly-method-calls-a-local-readonly-member-on-a-fresh-local")]
    [InlineData("readonly-method-sets-an-auto-property-on-what-it-owns")]
    [InlineData("readonly-method-sets-a-local-indexer-on-a-fresh-local")]
    [InlineData("readonly-method-fills-an-array-it-allocated")]
    [InlineData("readonly-method-writes-and-locally-calls-an-owned-parameter")]
    public void AReadonlyMember_MayWriteAndLocallyCallWhatItOwns(string fragment)
    {
        Assert.Empty(new ReadonlyContextFragments().Analyze(fragment));
    }

    [Theory]
    [InlineData("readonly-method-writes-a-field-of-an-unrestricted-parameter", FieldAssigned)]
    [InlineData("readonly-method-writes-a-field-of-a-frozen-parameter", FieldAssigned)]
    public void AReadonlyMember_StillMayNotWriteWhatItDoesNotOwn(string fragment, string expected)
    {
        var finding = Assert.Single(new ReadonlyContextFragments().Analyze(fragment));

        Assert.True(finding.Number.Value == expected, $"{fragment}: expected {expected}, got {finding.Number.Value}");
    }

    [Theory]
    [InlineData("readonly-method-calls-an-unmarked-member-on-a-fresh-local", "Poke")]
    [InlineData("readonly-method-calls-a-local-member-on-an-unrestricted-parameter", "Add")]
    [InlineData("readonly-method-calls-an-unmarked-member-on-an-unrestricted-parameter", "Poke")]
    public void AReadonlyMember_ExplainsThatNonReadonlyCallsRequireALocalMemberOnAnOwnedReceiver(string fragment, string member)
    {
        var finding = Assert.Single(new ReadonlyContextFragments().Analyze(fragment));

        Assert.Equal(CallsMutable, finding.Number.Value);
        Assert.Equal(
            $"Cannot call '{member}' from readonly method 'Poke': it is not readonly. A non-readonly call requires a local member on an owned receiver.",
            finding.Message);
    }
}