using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class LegacyArgumentCoercionTests
{
    [Fact]
    public void NamedArgumentsFollowSchemaOrderAndKeepEmptyOptionalsPresent()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "FIXTURE_LEGACY_ORDER", "{alias?Alias {alias}. }{label?Label {label}. }Name {name}.",
            slots: [new Slot("name", SlotKind.Name), new Slot("label", SlotKind.Optional, SlotKind.Name), new Slot("alias", SlotKind.Optional, SlotKind.Name)]);

        var values = LegacyArguments.For(descriptor, ["North", "", null]);

        Assert.Equal<SlotValue>(
            [new NameValue("North"), new OptionalValue(SlotKind.Name, new NameValue("")), new OptionalValue(SlotKind.Name, null)], values);
    }

    [Fact]
    public void AStringFillsOneSequenceItem()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "FIXTURE_LEGACY_NAMES", "Names: {names}.", slots: [new Slot("names", SlotKind.Sequence, SlotKind.Name)]);

        var values = LegacyArguments.For(descriptor, ["North"]);

        var names = Assert.IsType<SequenceValue>(Assert.Single(values));
        Assert.Equal(SlotKind.Name, names.ElementKind);
        Assert.Equal(new NameValue("North"), Assert.Single(names.Items));
    }

    [Fact]
    public void AnEnumerableCoercesItemsInSourceOrder()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "FIXTURE_LEGACY_COUNTS", "Counts: {counts}.", slots: [new Slot("counts", SlotKind.Sequence, SlotKind.Quantity)]);

        var values = LegacyArguments.For(descriptor, [new[] { 8, 3 }]);

        var counts = Assert.IsType<SequenceValue>(Assert.Single(values));
        Assert.Equal(SlotKind.Quantity, counts.ElementKind);
        Assert.Equal<SlotValue>([new QuantityValue(8), new QuantityValue(3)], counts.Items);
    }

    [Fact]
    public void QuantitiesKeepValuesWiderThanInt64FromNumbersAndIntegerText()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "FIXTURE_LEGACY_COUNT", "Count: {count}.", slots: [new Slot("count", SlotKind.Quantity)]);
        var wide = (Int128)long.MaxValue + 1;

        var number = LegacyArguments.For(descriptor, [wide]);
        var text = LegacyArguments.For(descriptor, ["9223372036854775808"]);

        Assert.Equal(new QuantityValue(wide), Assert.Single(number));
        Assert.Equal(new QuantityValue(wide), Assert.Single(text));
    }

    [Fact]
    public void TypedValuesAreReusedDirectlyAndInsideSequences()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "FIXTURE_LEGACY_TYPES", "{type} has alternatives {alternatives}.",
            slots: [new Slot("type", SlotKind.Type), new Slot("alternatives", SlotKind.Sequence, SlotKind.Type)]);
        var type = new TypeValue("Standard.Int32", "int");
        var alternative = new TypeValue("Standard.Int64", "long");

        var values = LegacyArguments.For(descriptor, [type, new[] { alternative }]);

        Assert.Collection(values,
            value => Assert.Same(type, value),
            value => Assert.Same(alternative, Assert.Single(Assert.IsType<SequenceValue>(value).Items)));
    }
}