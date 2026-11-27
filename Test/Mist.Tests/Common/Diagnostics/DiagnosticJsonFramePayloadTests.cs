using System.Text.Json;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class DiagnosticJsonFramePayloadTests
{
    [Fact]
    public void OperationFramePreservesProjectReferenceOrderAndPackage()
    {
        var frame = SerializeFrame(new OperationFrame("referenced-project", ["Zebra.Core", "Atlas.Model", "Birch.Runtime"], "Widget.Tools"));

        Assert.Equal("referenced-project", frame.GetProperty("project").GetString());
        Assert.Equal(["Zebra.Core", "Atlas.Model", "Birch.Runtime"], frame.GetProperty("referenceChain").EnumerateArray().Select(reference => reference.GetString()));
        Assert.Equal("Widget.Tools", frame.GetProperty("package").GetString());
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(100, false)]
    public void TruncatedFramePreservesDepthAndCyclicFlag(int depth, bool cyclic)
    {
        var frame = SerializeFrame(new TruncatedFrame(depth, cyclic));

        Assert.Equal(depth, frame.GetProperty("depth").GetInt32());
        Assert.Equal(cyclic, frame.GetProperty("cyclic").GetBoolean());
    }

    private static JsonElement SerializeFrame(Frame frame)
    {
        var diagnostic = new Diagnostic(
            DiagnosticFixtures.Descriptor("MIST9001", "message"), [], DiagnosticSeverity.Error, PipelineStage.Driver,
            new Absent(AbsenceReason.Unavailable), related: [], frames: [frame], analyzerName: null, native: null,
            rendersFallback: false, legacyText: null);
        diagnostic.Admit(1, DiagnosticSeverity.Error, reason: null);
        var document = new DiagnosticDocument("mist", "build", "root-project", "failed", [diagnostic], []);

        using var json = JsonDocument.Parse(DiagnosticJsonWriter.Serialize(document));
        return json.RootElement.GetProperty("diagnostics")[0].GetProperty("frames")[0].Clone();
    }
}