using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class RenderingTests
{
    [Fact]
    public void EveryRegistryDescriptorRendersWithRepresentativeValues()
    {
        foreach(var entry in DiagnosticRegistry.Index.Entries)
        {
            var admission = DiagnosticFactory.Create(
                entry.Descriptor, default, "probe.mist", PipelineStage.Driver, Representative(entry.Descriptor).AsSpan());

            Assert.Null(admission.Breach);
            var message = admission.Occurrence.Message;
            Assert.NotEqual(DiagnosticRenderer.Fallback(entry.Descriptor), message);
            Assert.False(string.IsNullOrWhiteSpace(message), entry.Field);
        }
    }

    [Fact]
    public void ASchemaBreachAdmitsTheFallbackAndOneInternalError()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "MIST_FIXTURE_NAMED", "'{name}' has type '{type}'.", slots: [new Slot("name", SlotKind.Name), new Slot("type", SlotKind.Type)]);

        var admission = DiagnosticFactory.Create(
            descriptor, default, "probe.mist", PipelineStage.Analysis, [new TextValue("wrong kind"), new TypeValue("Standard.Int32", "int")]);

        Assert.Equal(DiagnosticRenderer.Fallback(descriptor), admission.Occurrence.Message);
        var breach = Assert.IsType<Diagnostic>(admission.Breach);
        Assert.Same(InternalDiagnostics.ArgumentSchemaBreach, breach.Descriptor);
        Assert.Equal(DiagnosticCategory.Ice, breach.Descriptor.Category);
        Assert.Contains("MIST_FIXTURE_NAMED", breach.Message);
        Assert.Contains("Analysis", breach.Message);
        Assert.Contains("slot 'name' is Name but the argument is Text", breach.Message);
    }

    [Fact]
    public void AMissingPositionalArgumentIsABreachNotAThrow()
    {
        var descriptor = DiagnosticFixtures.Descriptor("MIST_FIXTURE_POSITIONAL", "'{0}' and '{1}'.");

        var admission = DiagnosticFactory.Create(descriptor, default, "", PipelineStage.Parsing, [new TextValue("only one")]);

        Assert.NotNull(admission.Breach);
        Assert.Equal(DiagnosticRenderer.Fallback(descriptor), admission.Occurrence.Message);
        Assert.Equal(2, admission.Occurrences.Count());
    }

    [Fact]
    public void NamedSubstitutionHonorsEscapesRepeatsAndOptionalSegments()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "MIST_FIXTURE_OPTIONAL",
            "'{name}' is not callable{namespace?; '{name}' lives in {namespace}}. Write {{name}}.",
            slots: [new Slot("name", SlotKind.Name), new Slot("namespace", SlotKind.Optional, SlotKind.Name)]);

        var absent = DiagnosticRenderer.Render(descriptor, [new NameValue("Add"), new OptionalValue(SlotKind.Name, null)]);
        var present = DiagnosticRenderer.Render(descriptor, [new NameValue("Add"), new OptionalValue(SlotKind.Name, new NameValue("Standard.Linq"))]);

        Assert.Equal("'Add' is not callable. Write {name}.", absent);
        Assert.Equal("'Add' is not callable; 'Add' lives in Standard.Linq. Write {name}.", present);
    }

    [Fact]
    public void PositionalSubstitutionMatchesCompositeFormatting()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "MIST_FIXTURE_FORMAT", "Reference '{0}' in '{1}' must be a table (e.g., {0} = {{ path = \"...\" }}).");
        var bag = new DiagnosticBag(PipelineStage.Driver);

        bag.Report(descriptor, default, "mist.toml", "Lib", "mist.toml");

        var occurrence = Assert.Single(bag.ToImmutableArray());
        Assert.Equal("Reference 'Lib' in 'mist.toml' must be a table (e.g., Lib = { path = \"...\" }).", occurrence.Message);
        Assert.Equal(PipelineStage.Driver, occurrence.Stage);
    }

    [Fact]
    public void ArgumentsAreSnapshotsRenderedUnderTheInvariantCulture()
    {
        var descriptor = DiagnosticFixtures.Descriptor("MIST_FIXTURE_CULTURE", "{0} items, {1}, {2}.");
        var bag = new DiagnosticBag();
        var mutable = new System.Text.StringBuilder("before");

        bag.Report(descriptor, default, "", 1234567, 2.5, mutable);
        _ = mutable.Clear().Append("after");

        Assert.Equal("1234567 items, 2.5, before.", Assert.Single(bag.ToImmutableArray()).Message);
    }

    [Fact]
    public void MissingNullAndEmptyAreDistinctValues()
    {
        var descriptor = DiagnosticFixtures.Descriptor("MIST_FIXTURE_NULLS", "[{0}][{1}]");
        var bag = new DiagnosticBag();

        bag.Report(descriptor, default, "", null, "");

        var occurrence = Assert.Single(bag.ToImmutableArray());
        Assert.Equal("[][]", occurrence.Message);
        _ = Assert.IsType<OptionalValue>(occurrence.Arguments[0]);
        Assert.Equal(new TextValue(""), occurrence.Arguments[1]);
    }

    [Fact]
    public void TheRendererNeverRecursesIntoItsOwnFailure()
    {
        var descriptor = DiagnosticFixtures.Descriptor("MIST_FIXTURE_BROKEN", "unbalanced {0");

        var text = DiagnosticRenderer.Render(descriptor, [new TextValue("x")]);

        Assert.Equal(DiagnosticRenderer.Fallback(descriptor), text);
        Assert.DoesNotContain("{", text);
    }

    [Fact]
    public void TheConstructorIsInternalAndTheMessageIsComputed()
    {
        Assert.Empty(typeof(Diagnostic).GetConstructors());
        Assert.Null(typeof(Diagnostic).GetProperty(nameof(Diagnostic.Message))!.SetMethod);
    }

    internal static ImmutableArray<SlotValue> Representative(MistDiagnostic descriptor)
    {
        if(descriptor.Slots.IsEmpty)
        {
            Assert.True(TemplateSyntax.TryParse(descriptor.Template, out var segments, out _));
            var count = TemplateSyntax.Placeholders(segments).Select(int.Parse).DefaultIfEmpty(-1).Max() + 1;
            return [.. Enumerable.Range(0, count).Select(index => (SlotValue)new TextValue($"arg{index}"))];
        }

        return [.. descriptor.Slots.Select(slot => Representative(slot.Kind, slot.ElementKind, slot.Name))];
    }

    private static SlotValue Representative(SlotKind kind, SlotKind? elementKind, string name)
    {
        return kind switch
        {
            SlotKind.Name => new NameValue(name),
            SlotKind.Type => new TypeValue($"Probe.{name}", name),
            SlotKind.Symbol => new SymbolValue($"Probe.{name}", name),
            SlotKind.Path => new PathValue($"{name}.mist"),
            SlotKind.Quantity => new QuantityValue(7),
            SlotKind.Boolean => new BooleanValue(true),
            SlotKind.Enum => new EnumValue(name),
            SlotKind.Sequence => new SequenceValue(elementKind ?? SlotKind.Text, [Representative(elementKind ?? SlotKind.Text, null, name)]),
            SlotKind.Optional => new OptionalValue(elementKind ?? SlotKind.Text, Representative(elementKind ?? SlotKind.Text, null, name)),
            _ => new TextValue(name),
        };
    }
}