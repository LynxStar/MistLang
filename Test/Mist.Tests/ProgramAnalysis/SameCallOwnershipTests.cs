using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class SameCallOwnershipTests
{
    [Theory]
    [InlineData("owned-then-owned", "MIST0227")]
    [InlineData("owned-then-frozen", "MIST0227")]
    [InlineData("owned-into-two-constructor-parameters", "MIST0227")]
    [InlineData("borrowed-then-frozen", "MIST0229")]
    [InlineData("borrowed-then-borrowed", "MIST0229")]
    [InlineData("borrowed-then-plain", "MIST0229")]
    [InlineData("frozen-then-borrowed", "MIST0229")]
    [InlineData("frozen-then-owned", "MIST0229")]
    [InlineData("readonly-ref-then-owned", "MIST0229")]
    [InlineData("readonly-ref-then-borrowed", "MIST0229")]
    [InlineData("receiver-then-owned-argument", "MIST0229")]
    [InlineData("borrowing-receiver-then-frozen-argument", "MIST0229")]
    [InlineData("owned-field-then-holder", "MIST0234")]
    public void ALaterOperandMeetsWhatAnEarlierOneInTheSameCallTook(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new CallBoundaryFragments().SameCall(caseName));

        Assert.True(diagnostic.Number.Value == expected, $"{caseName}: expected {expected}, got {diagnostic.Number.Value}");
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("readonly-ref-then-readonly-ref")]
    [InlineData("frozen-then-frozen")]
    [InlineData("frozen-then-readonly-ref")]
    [InlineData("readonly-ref-then-frozen")]
    [InlineData("readonly-ref-then-plain")]
    [InlineData("plain-then-plain")]
    [InlineData("receiver-then-frozen-argument")]
    [InlineData("receiver-then-plain-argument")]
    [InlineData("borrowed-then-returned-then-moved")]
    [InlineData("plain-then-bumped")]
    public void PairsTheLendingTablePermitsStayAdmittedInOneCall(string caseName)
    {
        Assert.Empty(new CallBoundaryFragments().SameCall(caseName));
    }
}