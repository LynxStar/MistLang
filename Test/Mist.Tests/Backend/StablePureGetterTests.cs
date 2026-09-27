// Tier 2 (Functional): purity Requirement 12.2 -- a refinement predicate may read a property whose getter
// is a pure member, declared or inferred, and still may not read one whose getter reads its instance's
// current state, a local readonly member being not enough there. The declaration-shape rule judges a
// hand-built alias whose predicate reads the getter, since a predicate is judged where it is declared.

using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class StablePureGetterTests
{
    private const string OverMutable = "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER";

    [Theory]
    [InlineData("getter-calling-a-declared-pure-method")]
    [InlineData("getter-calling-an-inferred-pure-method")]
    [InlineData("getter-calling-a-method-that-calls-another-pure-method")]
    [InlineData("getter-calling-a-static-pure-method")]
    public void AGetterCallingAPureMember_IsStableEnoughForAPredicate(string members)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(members));
    }

    [Theory]
    [InlineData("getter-reading-a-mutable-field")]
    [InlineData("getter-calling-a-method-reading-a-mutable-field")]
    [InlineData("getter-calling-a-local-readonly-method-reading-a-mutable-field")]
    [InlineData("getter-adding-a-mutable-static")]
    [InlineData("getter-calling-a-method-reading-a-mutable-static")]
    public void AGetterReadingWhatCanChange_IsRefusedForAPredicate(string members)
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze(members));

        Assert.Equal(OverMutable, finding.Id.Value);
        Assert.Contains("Both", finding.Message, StringComparison.Ordinal);
    }
}