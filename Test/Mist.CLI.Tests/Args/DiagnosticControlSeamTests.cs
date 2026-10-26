using System.Text.Json;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.CLI.Tests.Args;

[Trait("Tier", "4")]
public sealed class DiagnosticControlSeamTests
{
    [Fact]
    public void InvalidControlsProduceOneCfgDocumentBeforeDispatch()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var dispatched = false;
        var exit = CliHost.Run(["build", "--diagnostic-format", "json", "--disable", "MIST9999,MIST0000", "--min-severity", "error"],
            (_, _) => { dispatched = true; return 0; }, stderr);
        Assert.False(dispatched);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("usageError", json.RootElement.GetProperty("outcome").GetString());
        var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("CFG", diagnostic.GetProperty("category").GetString());
        Assert.True(diagnostic.GetProperty("visible").GetBoolean());
        Assert.Equal("", stdout.ToString());
    }

    [Fact]
    public void CliPolicySurvivesRootManifestAndPromotedInfoFailsEvenWithErrorOnlyDisplay()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"mist-policy-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "mist.toml"), "name = \"probe\"\nnamespace = \"Probe\"\n[diagnostics]\nwarnings_as_errors = false\n");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exit = CliHost.Run(["build", folder, "--warnings-as-errors", "--diagnostic-format", "json", "--min-severity", "error"],
                (args, output) =>
                {
                    var bag = new DiagnosticBag(output.Operation);
                    Assert.NotNull(ProjectFileParser.ParseRoot(args.ProjectFolder, bag));
                    bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "info", DiagnosticSeverity.Info, policy: PolicyClass.Promotable), new Absent(AbsenceReason.TestOrigin));
                    output.Collect(bag.Admitted);
                    Assert.True(bag.HasErrors);
                    return 0;
                }, stderr);
            Assert.Equal(1, exit);
            using var json = JsonDocument.Parse(stderr.ToString());
            var info = json.RootElement.GetProperty("diagnostics").EnumerateArray().Single(d => d.GetProperty("code").GetString() == "MIST9001");
            Assert.Equal("info", info.GetProperty("severity").GetProperty("default").GetString());
            Assert.Equal("error", info.GetProperty("severity").GetProperty("effective").GetString());
            Assert.True(info.GetProperty("visible").GetBoolean());
            Assert.Equal("failed", json.RootElement.GetProperty("outcome").GetString());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}