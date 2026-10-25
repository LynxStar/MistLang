using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class RegistryValidationTests
{
    [Fact]
    public void TheRegistryIndexIsValid()
    {
        var problems = DiagnosticRegistry.Index.Validate();

        Assert.True(problems.IsEmpty, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheIndexIsBuiltOnceAndHoldsEveryRegistryField()
    {
        var index = DiagnosticRegistry.Index;

        Assert.Same(index, DiagnosticRegistry.Index);
        Assert.True(index.Entries.Length > 400, $"{index.Entries.Length} entries");
        Assert.Contains(index.Entries, entry => entry.Field == "ProgramModelDiagnostics.NotCallable");
        Assert.Contains(index.Entries, entry => entry.Field == "LexerDiagnostics.LexError");
        Assert.True(index.TryGet(DriverDiagnostics.ProjectFileNotFound.Number, out var found));
        Assert.Same(DriverDiagnostics.ProjectFileNotFound, found);
        Assert.All(index.Entries, entry => Assert.True(entry.DeclaringType.Namespace == "Mist.Common.Diagnostics"));
    }

    [Fact]
    public void EveryDescriptorDeclaresEveryField()
    {
        Assert.All(DiagnosticRegistry.Index.Descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Title));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Template));
            Assert.True(Enum.IsDefined(descriptor.Category));
            Assert.True(Enum.IsDefined(descriptor.Policy));
        });
    }

    [Fact]
    public void AFixtureIsNotInTheIndex()
    {
        var fixture = DiagnosticFixtures.Descriptor("MIST_FIXTURE_PROBE", "probe");

        Assert.False(DiagnosticRegistry.Index.TryGet(fixture.Number, out _));
    }

    [Fact]
    public void TheCategoryTokensAreTheThirteenOfTheSpecification()
    {
        var tokens = Enum.GetValues<DiagnosticCategory>().Select(category => category.Token()).ToArray();

        Assert.Equal(["SYN", "NAM", "TYP", "DEC", "FLW", "OWN", "EFF", "INT", "BCK", "CFG", "DEP", "IOX", "ICE"], tokens);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    [InlineData("tombstone")]
    [InlineData("missing-title")]
    [InlineData("long-title")]
    [InlineData("missing-description")]
    [InlineData("description-repeats-template")]
    [InlineData("unknown-category")]
    [InlineData("placeholder-without-slot")]
    [InlineData("slot-without-placeholder")]
    [InlineData("bad-brace-escape")]
    [InlineData("promotable-error")]
    [InlineData("optional-error")]
    [InlineData("ice-warning")]
    [InlineData("conditional-on-non-optional")]
    [InlineData("related-unknown")]
    public void APlantedFaultFailsValidation(string fault)
    {
        var (entries, tombstones) = Plant(fault);

        var problems = RegistryValidator.Validate(entries, tombstones);

        _ = Assert.Single(problems);
    }

    [Fact]
    public void ASoundPlantedRegistryPasses()
    {
        var (entries, tombstones) = Plant("none");

        Assert.Empty(RegistryValidator.Validate(entries, tombstones));
    }

    private static (RegistryEntry[] Entries, Tombstone[] Tombstones) Plant(string fault)
    {
        var first = Entry("First", "MIST_PROBE_FIRST", "First probe", "Value '{0}' is probed.", "A probe found a value; remove it.");
        var second = Entry("Second", "MIST_PROBE_SECOND", "Second probe", "Name '{name}' is probed.", "A probe found a name; rename it.", new Slot("name", SlotKind.Name));
        Tombstone[] tombstones = [new Tombstone(DiagnosticNumber.Mint("MIST_PROBE_RETIRED"), "Retired probe", "0000000")];

        var planted = fault switch
        {
            "none" => second,
            "duplicate" => second with { Descriptor = second.Descriptor with { Number = first.Descriptor.Number } },
            "malformed" => second with { Descriptor = second.Descriptor with { Number = DiagnosticNumber.Mint("mist-second") } },
            "tombstone" => second with { Descriptor = second.Descriptor with { Number = tombstones[0].Number } },
            "missing-title" => second with { Descriptor = second.Descriptor with { Title = "" } },
            "long-title" => second with { Descriptor = second.Descriptor with { Title = "A title of far too many words here" } },
            "missing-description" => second with { Descriptor = second.Descriptor with { Description = " " } },
            "description-repeats-template" => second with { Descriptor = second.Descriptor with { Description = second.Descriptor.Template } },
            "unknown-category" => second with { Descriptor = second.Descriptor with { Category = (DiagnosticCategory)99 } },
            "placeholder-without-slot" => second with { Descriptor = second.Descriptor with { Template = "Name '{name}' and '{other}'." } },
            "slot-without-placeholder" => second with { Descriptor = second.Descriptor with { Template = "Nothing named." } },
            "bad-brace-escape" => second with { Descriptor = second.Descriptor with { Template = "Name '{name}' } here." } },
            "promotable-error" => second with { Descriptor = second.Descriptor with { Policy = PolicyClass.Promotable } },
            "optional-error" => second with { Descriptor = second.Descriptor with { Policy = PolicyClass.Optional } },
            "ice-warning" => second with { Descriptor = second.Descriptor with { Category = DiagnosticCategory.Ice, DefaultSeverity = DiagnosticSeverity.Warning } },
            "conditional-on-non-optional" => second with { Descriptor = second.Descriptor with { Template = "Name{name? '{name}'}." } },
            "related-unknown" => second with { Descriptor = Related(second.Descriptor, DiagnosticNumber.Mint("MIST_PROBE_NOWHERE")) },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        return ([first, planted], tombstones);
    }

    private static MistDiagnostic Related(MistDiagnostic descriptor, DiagnosticNumber related)
    {
        return new MistDiagnostic(
            descriptor.Number, descriptor.Title, descriptor.Template, descriptor.Description, descriptor.Category,
            descriptor.Policy, descriptor.DefaultSeverity, descriptor.Slots, [related]);
    }

    private static RegistryEntry Entry(string field, string number, string title, string template, string description, params Slot[] slots)
    {
        var descriptor = new MistDiagnostic(
            DiagnosticNumber.Mint(number),
            Title: title,
            Template: template,
            Description: description,
            Category: DiagnosticCategory.Typ,
            Policy: PolicyClass.Fixed,
            DefaultSeverity: DiagnosticSeverity.Error,
            Slots: [.. slots]);

        return new RegistryEntry(typeof(RegistryValidationTests), field, descriptor);
    }
}