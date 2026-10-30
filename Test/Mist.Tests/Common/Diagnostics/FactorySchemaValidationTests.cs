using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class FactorySchemaValidationTests
{
    private static readonly MistDiagnostic Named = DiagnosticFixtures.Descriptor(
        "FIXTURE_SCHEMA",
        "'{name}' has candidates {items}{namespace?; see {namespace}}.",
        slots:
        [
            new Slot("name", SlotKind.Name),
            new Slot("items", SlotKind.Sequence, SlotKind.Name),
            new Slot("namespace", SlotKind.Optional, SlotKind.Name),
        ]);

    private static readonly MistDiagnostic Nested = DiagnosticFixtures.Descriptor(
        "FIXTURE_SCHEMA_NESTED", "Options: {options}.", slots: [new Slot("options", SlotKind.Sequence, SlotKind.Optional)]);

    private static readonly MistDiagnostic Positional = DiagnosticFixtures.Descriptor("FIXTURE_SCHEMA_POSITIONAL", "'{0}' then '{1}'.");

    private static readonly ImmutableArray<Frame> Frames = [new SynthesisFrame("lowering")];

    private static readonly Location Origin = new FileOnly("schema.mist");

    public static TheoryData<string, bool> MalformedArguments => Both(
        "optional payload kind",
        "sequence item kind",
        "sequence null item",
        "sequence without items",
        "null argument",
        "optional element kind",
        "nested payload kind",
        "positional null",
        "positional item kind");

    public static TheoryData<string, bool> WellFormedArguments => Both(
        "absent optional and empty sequence",
        "present optional and filled sequence",
        "positional composite");

    [Theory]
    [MemberData(nameof(MalformedArguments))]
    public void BothFactoryOverloadsRejectMalformedPayloads(string shape, bool resolved)
    {
        var (descriptor, values, reason) = Malformed(shape);

        var admission = Create(descriptor, values, resolved);

        Assert.Equal(DiagnosticRenderer.Fallback(descriptor), admission.Occurrence.Message);
        Assert.Equal<SlotValue>(values, admission.Occurrence.Arguments);
        Assert.Same(Origin, admission.Occurrence.Location);
        Assert.Equal(Frames, admission.Occurrence.Frames);
        var breach = Assert.Single(admission.Breaches);
        Assert.Same(InternalDiagnostics.ArgumentSchemaBreach, breach.Descriptor);
        Assert.Equal(DiagnosticCategory.Ice, breach.Descriptor.Category);
        Assert.Same(Origin, breach.Location);
        Assert.Equal(Frames, breach.Frames);
        Assert.Contains(descriptor.Number.Value, breach.Message);
        Assert.Contains("Analysis", breach.Message);
        Assert.Contains(reason, breach.Message);
        AssertAdmitsAndFormats(admission, descriptor);
    }

    [Theory]
    [MemberData(nameof(WellFormedArguments))]
    public void BothFactoryOverloadsKeepWellFormedPayloads(string shape, bool resolved)
    {
        var (descriptor, values, message) = WellFormed(shape);

        var admission = Create(descriptor, values, resolved);

        Assert.Empty(admission.Breaches);
        Assert.Equal(message, admission.Occurrence.Message);
        Assert.Equal<SlotValue>(values, admission.Occurrence.Arguments);
        Assert.Same(Origin, admission.Occurrence.Location);
        Assert.Equal(Frames, admission.Occurrence.Frames);
        AssertAdmitsAndFormats(admission, descriptor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALiveOptionalSlotRejectsAForeignPayloadAndKeepsItsOwn(bool resolved)
    {
        var descriptor = ProgramModelDiagnostics.NotCallable;
        var valid = RenderingTests.Representative(descriptor);
        var index = descriptor.Slots.ToList().FindIndex(slot => slot.Kind == SlotKind.Optional);
        Assert.True(index >= 0);
        var elementKind = descriptor.Slots[index].ElementKind!.Value;
        var foreign = valid.SetItem(index, new OptionalValue(elementKind, new QuantityValue(7)));

        var accepted = Create(descriptor, valid, resolved);
        var rejected = Create(descriptor, foreign, resolved);

        Assert.Empty(accepted.Breaches);
        Assert.DoesNotContain("7", accepted.Occurrence.Message);
        Assert.Equal(DiagnosticRenderer.Fallback(descriptor), rejected.Occurrence.Message);
        var breach = Assert.Single(rejected.Breaches);
        Assert.Same(InternalDiagnostics.ArgumentSchemaBreach, breach.Descriptor);
        Assert.Contains($"slot '{descriptor.Slots[index].Name}' carries {elementKind} but its value is Quantity", breach.Message);
        AssertAdmitsAndFormats(rejected, descriptor);
    }

    private static (MistDiagnostic Descriptor, ImmutableArray<SlotValue> Values, string Reason) Malformed(string shape)
    {
        var names = new SequenceValue(SlotKind.Name, [new NameValue("a")]);
        var absent = new OptionalValue(SlotKind.Name, null);

        return shape switch
        {
            "optional payload kind" => (
                Named, [new NameValue("Add"), names, new OptionalValue(SlotKind.Name, new QuantityValue(7))],
                "slot 'namespace' carries Name but its value is Quantity"),
            "sequence item kind" => (
                Named, [new NameValue("Add"), new SequenceValue(SlotKind.Name, [new NameValue("a"), new QuantityValue(7)]), absent],
                "slot 'items' carries Name but item 1 is Quantity"),
            "sequence null item" => (
                Named, [new NameValue("Add"), new SequenceValue(SlotKind.Name, [new NameValue("a"), null!]), absent],
                "slot 'items' has a null item 1"),
            "sequence without items" => (
                Named, [new NameValue("Add"), new SequenceValue(SlotKind.Name, default), absent],
                "slot 'items' has no item list"),
            "null argument" => (
                Named, [new NameValue("Add"), null!, absent],
                "slot 'items' is null"),
            "optional element kind" => (
                Named, [new NameValue("Add"), names, new OptionalValue(SlotKind.Quantity, new QuantityValue(7))],
                "slot 'namespace' carries Name but the argument carries Quantity"),
            "nested payload kind" => (
                Nested, [new SequenceValue(SlotKind.Optional, [absent, new OptionalValue(SlotKind.Name, new QuantityValue(7))])],
                "slot 'options' item 1 carries Name but its value is Quantity"),
            "positional null" => (
                Positional, [new TextValue("a"), null!],
                "argument 1 is null"),
            "positional item kind" => (
                Positional, [new TextValue("a"), new SequenceValue(SlotKind.Text, [new QuantityValue(7)])],
                "argument 1 carries Text but item 0 is Quantity"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    private static (MistDiagnostic Descriptor, ImmutableArray<SlotValue> Values, string Message) WellFormed(string shape)
    {
        return shape switch
        {
            "absent optional and empty sequence" => (
                Named, [new NameValue("Add"), new SequenceValue(SlotKind.Name, []), new OptionalValue(SlotKind.Name, null)],
                "'Add' has candidates ."),
            "present optional and filled sequence" => (
                Named,
                [
                    new NameValue("Add"),
                    new SequenceValue(SlotKind.Name, [new NameValue("a"), new NameValue("b")]),
                    new OptionalValue(SlotKind.Name, new NameValue("Standard.Linq")),
                ],
                "'Add' has candidates a, b; see Standard.Linq."),
            "positional composite" => (
                Positional, [new TextValue("a"), new SequenceValue(SlotKind.Text, [new TextValue("b"), new TextValue("c")])],
                "'a' then 'b, c'."),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    private static Admission Create(MistDiagnostic descriptor, ImmutableArray<SlotValue> values, bool resolved)
    {
        return resolved
            ? DiagnosticFactory.Create(descriptor, new OriginResolution(Origin, Frames), PipelineStage.Analysis, values.AsSpan())
            : DiagnosticFactory.Create(descriptor, Origin, PipelineStage.Analysis, values.AsSpan(), frames: Frames);
    }

    private static void AssertAdmitsAndFormats(Admission admission, MistDiagnostic descriptor)
    {
        var bag = new DiagnosticBag(PipelineStage.Analysis);

        bag.Admit(admission);

        Assert.Equal(admission.Occurrences.Count(), bag.Admitted.Count);
        foreach(var occurrence in bag.Admitted)
        {
            Assert.Contains(occurrence.Number.Value, DiagnosticFormatter.Format(occurrence));
        }

        Assert.Contains(descriptor.Number.Value, DiagnosticFormatter.Format(admission.Occurrence));
    }

    private static TheoryData<string, bool> Both(params string[] shapes)
    {
        var data = new TheoryData<string, bool>();
        foreach(var shape in shapes)
        {
            data.Add(shape, false);
            data.Add(shape, true);
        }

        return data;
    }
}