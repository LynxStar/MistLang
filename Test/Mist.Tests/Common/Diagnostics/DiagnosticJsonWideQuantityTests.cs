using System.Text.Json;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class DiagnosticJsonWideQuantityTests
{
    [Fact]
    public void NegativeWideQuantityPreservesSignAndDigitsAsAJsonNumber()
    {
        var descriptor = DiagnosticFixtures.Descriptor("MIST9001", "Delta: {delta}", slots: [new("delta", SlotKind.Quantity)]);
        var value = -((Int128)ulong.MaxValue + 2);
        var diagnostic = new Diagnostic(
            descriptor, [new QuantityValue(value)], DiagnosticSeverity.Error, PipelineStage.Driver,
            new Absent(AbsenceReason.TestOrigin), related: [], frames: [], analyzerName: null, native: null,
            rendersFallback: false, legacyText: null);
        diagnostic.Admit(1, DiagnosticSeverity.Error, reason: null);
        var document = new DiagnosticDocument("mist", "build", ".", "failed", [diagnostic], []);

        using var json = JsonDocument.Parse(DiagnosticJsonWriter.Serialize(document));
        var delta = json.RootElement.GetProperty("diagnostics")[0].GetProperty("arguments").GetProperty("delta");

        Assert.Equal(JsonValueKind.Number, delta.ValueKind);
        Assert.Equal("-18446744073709551617", delta.GetRawText());
    }
}