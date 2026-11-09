using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class RefinementCaseDescriptorTests
{
    public static TheoryData<string> CaseDescriptors => new()
    {
        "RefinementViolated", "RefinementUnproven", "RefinementNotImplied", "IndexOutOfBounds", "IndexUnproven", "ConstraintViolated", "ConstraintUnproven",
    };

    [Fact]
    public void ProofExhaustedIsAValidTypedFixedError()
    {
        var descriptor = ProgramAnalysisDiagnostics.RefinementProofExhausted;
        var entry = Assert.Single(DiagnosticRegistry.Index.Entries, entry => ReferenceEquals(entry.Descriptor, descriptor));

        Assert.Equal("MIST0485", descriptor.Number.Value);
        Assert.Equal("ProgramAnalysisDiagnostics.RefinementProofExhausted", entry.Field);
        Assert.Equal(DiagnosticCategory.Typ, descriptor.Category);
        Assert.Equal(PolicyClass.Fixed, descriptor.Policy);
        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
        Assert.Equal(["predicate", "value"], descriptor.Slots.Select(slot => slot.Name).ToArray());
        Assert.All(descriptor.Slots, slot => Assert.Equal(SlotKind.Name, slot.Kind));
        Assert.Empty(RegistryValidator.Validate([entry], DiagnosticRegistry.Index.Tombstones));
    }

    [Fact]
    public void ProofExhaustedTookTheLowestNumberAboveEveryIssuedOne()
    {
        var issued = DiagnosticRegistry.Index.Descriptors.Where(descriptor => !ReferenceEquals(descriptor, ProgramAnalysisDiagnostics.RefinementProofExhausted))
            .Select(descriptor => descriptor.Number.Value).Concat(DiagnosticRegistry.Index.Tombstones.Select(tombstone => tombstone.Number.Value))
            .Max(number => int.Parse(number[4..], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal($"MIST{issued + 1:D4}", ProgramAnalysisDiagnostics.RefinementProofExhausted.Number.Value);
    }

    [Fact]
    public void ProofExhaustedRendersThePredicateAndTheValue()
    {
        var text = DiagnosticRenderer.Render(ProgramAnalysisDiagnostics.RefinementProofExhausted, [new NameValue("< Length"), new NameValue("start")]);

        Assert.Equal("Could not finish proving '< Length' for 'start' within the compiler's limit: add an explicit check on this path.", text);
    }

    [Theory]
    [MemberData(nameof(CaseDescriptors))]
    public void EachRefusalEndsWithAnOptionalCaseThatRendersEmptyWhenAbsent(string field)
    {
        var descriptor = Assert.Single(DiagnosticRegistry.Index.Entries, entry => entry.Field == $"ProgramAnalysisDiagnostics.{field}").Descriptor;
        var last = descriptor.Slots[^1];
        var leading = descriptor.Slots[..^1].Select(slot => (SlotValue)(slot.Kind == SlotKind.Type ? new TypeValue("int", "int") : new NameValue("x"))).ToList();

        var absent = DiagnosticRenderer.Render(descriptor, [.. leading, new OptionalValue(SlotKind.Name, null)]);
        var present = DiagnosticRenderer.Render(descriptor, [.. leading, new OptionalValue(SlotKind.Name, new NameValue(" (fails when x <= -1)"))]);

        Assert.Equal(new Slot("case", SlotKind.Optional, SlotKind.Name), last);
        Assert.DoesNotContain("fails when", absent, StringComparison.Ordinal);
        Assert.Contains(" (fails when x <= -1)", present, StringComparison.Ordinal);
        Assert.Equal(absent.Length + " (fails when x <= -1)".Length, present.Length);
    }
}