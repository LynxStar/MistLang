// Tier 2 (Functional): two contracts the purity spec states and no single body can be trusted to keep --
// Requirement 8.4, an abstract member's declared promise binds every implementation in the program, and
// Requirement 16.5 met at a runtime type test: the promise is erased on emission, so `is`, `as` and a
// cast against a pure function type cannot tell a pure value from a plain one and are refused unless the
// static type already answers. Each rule judges a hand-built fragment.

using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class PurityContractTests
{
    private const string ContractNotMet = "MIST0144";
    private const string Untestable = "MIST0145";
    private const string NoConversion = "MIST0149";

    [Theory]
    [InlineData("pure-slot-answered-by-a-method-reading-a-mutable-static", "Area")]
    [InlineData("pure-slot-answered-by-a-readonly-method-reading-a-mutable-static", "Area")]
    [InlineData("local-slot-answered-by-a-method-writing-a-static", "Add")]
    public void AnImplementationThatBreaksTheAbstractsPromise_IsRefusedNamingTheMember(string implementation, string member)
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze(implementation));

        Assert.Equal(ContractNotMet, finding.Number.Value);
        Assert.Contains(member, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure-slot-answered-by-an-unmarked-method-returning-a-literal")]
    [InlineData("pure-slot-answered-by-a-pure-method-reading-a-readonly-static")]
    [InlineData("local-slot-answered-by-a-method-writing-its-own-field")]
    [InlineData("local-slot-answered-by-a-pure-method-with-an-empty-body")]
    public void AnImplementationThatKeepsTheAbstractsPromise_IsAdmittedWhateverItDeclares(string implementation)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(implementation));
    }

    [Theory]
    [InlineData("plain-function-in-an-object-tested-for-a-pure-function-type")]
    [InlineData("plain-function-in-an-object-converted-as-a-pure-function-type")]
    public void ARuntimeTestAgainstAPureFunctionType_IsRefusedAsUntestable(string test)
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze(test));

        Assert.Equal(Untestable, finding.Number.Value);
        Assert.Contains("pure func<", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACastToAPureFunctionType_IsRefusedAsHavingNoConversion()
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze("plain-function-in-an-object-cast-to-a-pure-function-type"));

        Assert.Equal(NoConversion, finding.Number.Value);
        Assert.Contains("pure func<", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeTestTheStaticTypeAlreadyAnswers_IsAdmitted()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("pure-function-value-tested-for-its-own-type"));
    }
}