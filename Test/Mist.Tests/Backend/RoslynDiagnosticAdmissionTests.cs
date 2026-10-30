using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class RoslynDiagnosticAdmissionTests
{
    [Fact]
    public void OnlyNativeErrorsBecomeMistOccurrencesAndEveryNativeValueSurvivesJson()
    {
        var location = new GeneratedLocation("out/Probe.cs", new LinePositionSpan(new LinePosition(3, 5), new LinePosition(3, 9)), null);
        NativeDiagnostic[] native =
        [
            new("roslyn", "CS1001", "error", "Identifier expected", location),
            new("roslyn", "CS8618", "warning", "Nullable warning", location),
            new("roslyn", "CS8019", "hidden", "Unused using", location),
            new("roslyn", "CS9000", "info", "Native information", location),
        ];
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        foreach(var entry in native)
        {
            RoslynDiagnosticAdapter.Admit(entry, diagnostics);
        }

        var occurrence = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(BackendDiagnostics.CSharpError.Number, occurrence.Number);
        Assert.Equal(new ProviderCode("roslyn", "CS1001", DiagnosticSeverity.Error), occurrence.Native);
        Assert.Equal(location, occurrence.Location);
        Assert.Equal("Identifier expected", Assert.IsType<TextValue>(Assert.Single(occurrence.Arguments)).Text);
        Assert.Equal(1, DiagnosticCounts.From(diagnostics.Admitted, DiagnosticSeverity.Info).Errors);
        var document = new DiagnosticDocument("test", "build", "Probe", "failed", diagnostics.ToImmutableArray(), [.. native]);
        var json = DiagnosticJsonWriter.Serialize(document);
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, parsed.RootElement.GetProperty("counts").GetProperty("admitted").GetInt32());
        Assert.Equal(4, parsed.RootElement.GetProperty("native").GetArrayLength());
        var warning = parsed.RootElement.GetProperty("native")[1];
        Assert.Equal("warning", warning.GetProperty("severity").GetString());
        Assert.Equal("CS8618", warning.GetProperty("code").GetString());
        Assert.Equal("generated", warning.GetProperty("location").GetProperty("kind").GetString());
    }
}