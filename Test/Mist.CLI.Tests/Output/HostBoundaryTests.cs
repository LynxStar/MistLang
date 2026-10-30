using System.Text.Json;
using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Output;

[Trait("Tier", "2")]
public sealed class HostBoundaryTests
{
    [Fact]
    public void ThrowingCommandCompletesActiveJsonWithOneIceAndSnapshottedCauseAndStack()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var controls = new DiagnosticControls(DiagnosticFormat.Json, Minimum: DiagnosticSeverity.Error);
        var arguments = new CliArguments("project", Quiet: true, Diagnostics: controls);
        var parsed = new CliParseResult(arguments, [], controls);
        var exit = CliHost.Run([], Throw, stderr, _ => parsed);
        Assert.Equal(1, exit);
        Assert.Equal("", stdout.ToString());
        using var json = JsonDocument.Parse(stderr.ToString());
        var root = json.RootElement;
        Assert.Equal("internalFailure", root.GetProperty("outcome").GetString());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("errors").GetInt32());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("hidden").GetInt32());
        var ice = Assert.Single(root.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("category").GetString() == "ICE");
        Assert.Equal(2, ice.GetProperty("ordinal").GetInt32());
        Assert.True(ice.GetProperty("visible").GetBoolean());
        var details = ice.GetProperty("arguments").GetProperty("details").EnumerateArray().Select(v => v.GetString()).ToArray();
        Assert.Equal("System.InvalidOperationException", details[0]);
        Assert.Equal("outer failure", details[1]);
        Assert.Contains(nameof(Throw), details[2]);
        Assert.Equal("System.ArgumentException", details[3]);
        Assert.Equal("cause snapshot", details[4]);
        Assert.Contains("outer failure", ice.GetProperty("message").GetString());
    }

    [Fact]
    public void LateHostFailureLeavesOneCompletedDocumentAndReportsOneTextIce()
    {
        using var stderr = new StringWriter();
        var controls = new DiagnosticControls(DiagnosticFormat.Json);
        var parsed = new CliParseResult(new CliArguments("project", Command: CliCommand.Run), [], controls);
        var exit = CliHost.Run([], (_, output) =>
        {
            output.Complete(true);
            throw new InvalidOperationException("late failure");
        }, stderr, _ => parsed);
        Assert.Equal(1, exit);
        var lines = stderr.ToString().Split(Environment.NewLine);
        using var document = JsonDocument.Parse(lines[0]);
        Assert.Equal("succeeded", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(1, lines.Count(line => line.StartsWith("error MIST0423:", StringComparison.Ordinal)));
        Assert.Contains("late failure", lines[1]);
        Assert.Equal(1, lines.Count(line => line.StartsWith('{')));
    }

    private static int Throw(CliArguments _, DiagnosticOutput output)
    {
        var bag = new DiagnosticBag(output.Operation);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "earlier note", DiagnosticSeverity.Info), new Absent(AbsenceReason.TestOrigin));
        output.Stream(bag.Admitted[0]);
        throw new InvalidOperationException("outer failure", new ArgumentException("cause snapshot"));
    }
}