using System.Text.Json;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Output;

[Trait("Tier", "2")]
public sealed class LateHostFailureOutputTests
{
    [Fact]
    public void RepeatedLateLaunchFailureAppendsOneTextLineToTheCompletedJson()
    {
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error, DiagnosticFormat.Json, command: "run");
        var location = new Absent(AbsenceReason.Unavailable);
        var compiler = new DiagnosticBag(output.Operation);
        compiler.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "compiler warning", DiagnosticSeverity.Warning), location);
        output.Collect(compiler.Admitted);
        output.Complete(true);

        var originalJson = error.ToString();
        using var document = JsonDocument.Parse(originalJson);
        var root = document.RootElement;
        Assert.Equal("succeeded", root.GetProperty("outcome").GetString());
        var warning = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("MIST9001", warning.GetProperty("code").GetString());

        var host = new DiagnosticBag(output.Operation);
        host.Admit(DiagnosticFactory.Create(CommandDiagnostics.ProcessLaunchFailed, location, PipelineStage.Driver,
            [new NameValue("mist-child"), new TextValue("access denied")]));
        var failure = Assert.Single(host.Admitted);
        var expected = originalJson + "error MIST0457: Failed to launch 'mist-child': access denied" + error.NewLine;

        output.ReportHostFailure(failure);
        Assert.Equal(expected, error.ToString());

        output.ReportHostFailure(failure with { });
        output.Complete(false);
        Assert.Equal(expected, error.ToString());
    }
}