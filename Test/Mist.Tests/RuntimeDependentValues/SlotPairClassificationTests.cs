using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class SlotPairClassificationTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly MemberType sized;

    public SlotPairClassificationTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        sized = HandBuiltSlots.Generic(new AbstractType("ISized<>", "Probe", null, null, null), "N", slots.Int);
    }

    private IMistType Slot(string kind)
    {
        return kind switch
        {
            "3" => slots.Literal(3),
            "5" => slots.Literal(5),
            "n" => slots.Capture(new LocalVariable("n", slots.Int)).Value,
            "m" => slots.Capture(new LocalVariable("m", slots.Int)).Value,
            "N" => slots.Dependent("N"),
            "unknown" => UnknownType.Instance,
            "unknown value" => new UnknownValueType(slots.Int),
            _ => throw new InvalidOperationException(kind),
        };
    }

    [Theory]
    [InlineData("3", "5", "None")]
    [InlineData("n", "3", "Implicit")]
    [InlineData("3", "n", "Implicit")]
    [InlineData("n", "m", "Implicit")]
    [InlineData("N", "3", "Implicit")]
    [InlineData("3", "N", "Implicit")]
    [InlineData("unknown", "3", "Implicit")]
    [InlineData("unknown value", "n", "Implicit")]
    [InlineData("unknown", "N", "Implicit")]
    [InlineData("n", "unknown value", "Implicit")]
    public void AClassClosingReachesAnotherExceptAcrossTwoLiterals(string from, string to, string expected)
    {
        Assert.Equal(expected, ConversionClassifier.Classify(slots.Closed(ring, Slot(from)), slots.Closed(ring, Slot(to))).ToString());
    }

    [Theory]
    [InlineData("unknown", "3")]
    [InlineData("unknown", "n")]
    [InlineData("unknown value", "N")]
    public void AnAbstractClosedWithUnknownReachesNoNamedValue(string from, string to)
    {
        Assert.Equal(ConversionKind.None, ConversionClassifier.Classify(slots.Closed(sized, Slot(from)), slots.Closed(sized, Slot(to))));
    }

    [Fact]
    public void OneCaptureIsOneTypeAndTwoOfOneNameAreTwo()
    {
        var read = new LocalVariable("n", slots.Int);
        var first = slots.Capture(read);
        var second = slots.Capture(read);

        Assert.Equal(ConversionKind.Identity, ConversionClassifier.Classify(slots.Closed(ring, first.Value), slots.Closed(ring, first.Value)));
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(slots.Closed(ring, first.Value), slots.Closed(ring, second.Value)));
        Assert.Equal("Ring<n>", TypeDisplay.Mist(slots.Closed(ring, second.Value)));
        Assert.Equal("int[n]", TypeDisplay.Mist(slots.ArrayOf(second.Value)));
    }

    [Theory]
    [InlineData("3", "5", "MIST0193")]
    [InlineData("N", "5", null)]
    [InlineData("n", "5", null)]
    [InlineData("n", "m", null)]
    [InlineData("unknown", "n", null)]
    public void APatternNeverMatchesOnlyAcrossTwoLiterals(string input, string tested, string? expected)
    {
        var test = new TypeTestPattern(HandBuiltModel.Node(), slots.Closed(ring, Slot(tested)), null);

        Assert.Equal(expected, PatternAnalyzer.TypeFinding(test, slots.Closed(ring, Slot(input)), slots.Refinements.Built.Interner)?.Number.Value);
    }
}