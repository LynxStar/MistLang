using System.Reflection;
using System.Text;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class DiagnosticCatalogTests
{
    [Fact]
    public void TheCommittedPageMatchesTheRegistryIndex()
    {
        var metadata = typeof(DiagnosticCatalogTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var encoded = Assert.Single(metadata, attribute => attribute.Key == "DiagnosticCatalogPage").Value!;
        var committed = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

        Assert.Equal(committed, DiagnosticCatalog.Render(DiagnosticRegistry.Index));
    }

    [Fact]
    public void CategoriesAndNumbersHaveStableOrderRegardlessOfEntryOrder()
    {
        var first = Descriptor("MIST9001", DiagnosticCategory.Syn);
        var second = Descriptor("MIST9002", DiagnosticCategory.Syn);
        var third = Descriptor("MIST9003", DiagnosticCategory.Interop);
        var forward = Index(first, second, third);
        var reverse = Index(third, second, first);

        var rendered = DiagnosticCatalog.Render(reverse);

        Assert.Equal(DiagnosticCatalog.Render(forward), rendered);
        Assert.True(rendered.IndexOf("## SYN", StringComparison.Ordinal) < rendered.IndexOf("## INT", StringComparison.Ordinal));
        Assert.True(rendered.IndexOf("| MIST9001 |", StringComparison.Ordinal) < rendered.IndexOf("| MIST9002 |", StringComparison.Ordinal));
        Assert.DoesNotContain('\r', rendered);
    }

    [Fact]
    public void EveryRequiredFieldAndTheFullSlotSchemaAppearInTheRow()
    {
        var descriptor = DiagnosticFixtures.Descriptor(
            "MIST9001", "probe", DiagnosticSeverity.Warning, DiagnosticCategory.Eff, PolicyClass.Optional,
            "Probe condition", new Slot("name", SlotKind.Name), new Slot("types", SlotKind.Sequence, SlotKind.Type),
            new Slot("detail", SlotKind.Optional, SlotKind.Text));

        var rendered = DiagnosticCatalog.Render(Index(descriptor));

        Assert.Contains("## EFF\n", rendered);
        Assert.Contains("| MIST9001 | Probe condition | A descriptor a test constructed for its own purposes. | Warning | Optional | name: Name, types: Sequence&lt;Type&gt;, detail: Optional&lt;Text&gt; | None |\n", rendered);
    }

    [Fact]
    public void ZeroSlotsAreExplicit()
    {
        var descriptor = Descriptor("MIST9001", DiagnosticCategory.Typ);

        var rendered = DiagnosticCatalog.Render(Index(descriptor));

        Assert.Contains("| Error | Fixed | None | None |\n", rendered);
    }

    [Fact]
    public void MetadataCannotBreakMarkdownTablesOrIntroduceMarkup()
    {
        var descriptor = Descriptor("MIST9001", DiagnosticCategory.Typ) with
        {
            Title = "A | B",
            Description = "<type> & `code` *value* _name_ [link] \\path\r\nnext\rline\nlast",
        };

        var rendered = DiagnosticCatalog.Render(Index(descriptor));

        Assert.Contains("| A \\| B | &lt;type&gt; &amp; \\`code\\` \\*value\\* \\_name\\_ \\[link\\] \\\\path<br>next<br>line<br>last |", rendered);
    }

    private static MistDiagnostic Descriptor(string number, DiagnosticCategory category)
    {
        return DiagnosticFixtures.Descriptor(number, "probe", category: category);
    }

    private static RegistryIndex Index(params MistDiagnostic[] descriptors)
    {
        return new RegistryIndex(
            [.. descriptors.Select((descriptor, index) => new RegistryEntry(typeof(DiagnosticCatalogTests), $"Probe{index}", descriptor))], []);
    }
}