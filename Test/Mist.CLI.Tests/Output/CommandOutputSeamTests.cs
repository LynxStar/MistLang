using System.Diagnostics;
using System.Text.Json;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests.Output;

[Trait("Tier", "4")]
public sealed class CommandOutputSeamTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("test")]
    public void CompileOutputCompletesBeforeChildAndPreservesChildStreamsAndExit(string command)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var output = new DiagnosticOutput(stderr, DiagnosticFormat.Json, command: command);
        var bag = new DiagnosticBag(output.Operation);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "warning", DiagnosticSeverity.Warning), new Absent(AbsenceReason.TestOrigin));
        var exit = new CompileFeedback(NullProgressReporter.Instance, output).Run(
            listener =>
            {
                listener.Receive(bag.Admitted[0]);
                Assert.Equal("", stderr.ToString());
                return new CompilerResult(true, "artifact", bag.ToImmutableArray());
            }, Stopwatch.StartNew(), result =>
            {
                Assert.Equal("artifact", result.PrimaryOutputArtifactPath);
                using var json = JsonDocument.Parse(stderr.ToString());
                Assert.Equal(command, json.RootElement.GetProperty("command").GetString());
                _ = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
                stdout.Write("child stdout");
                stderr.Write("child stderr");
                return 7;
            });
        Assert.Equal(7, exit);
        Assert.Equal("child stdout", stdout.ToString());
        Assert.EndsWith("child stderr", stderr.ToString());
    }

    [Fact]
    public void FileDocumentKeepsTextOnStderrAndContainsNoProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mist-diagnostics-{Guid.NewGuid():N}.json");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var output = new DiagnosticOutput(stderr, DiagnosticFormat.Json, documentFile: bytes => File.WriteAllBytes(path, bytes));
            var bag = new DiagnosticBag(output.Operation);
            bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "warning", DiagnosticSeverity.Warning), new Absent(AbsenceReason.TestOrigin));
            var exit = new CompileFeedback(new ConsoleProgressReporter(stdout), output).Run(
                _ => new CompilerResult(true, "artifact", bag.ToImmutableArray()), Stopwatch.StartNew(), _ => 0);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'{', bytes[0]);
            using var json = JsonDocument.Parse(bytes);
            _ = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Contains("warning MIST9001", stderr.ToString());
            Assert.DoesNotContain("schemaVersion", stderr.ToString());
            Assert.Contains("Build succeeded", stdout.ToString());
            Assert.Equal(0, exit);
        }
        finally
        {
            File.Delete(path);
        }
    }
}