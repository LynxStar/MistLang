using Mist.CLI.Output;

namespace Mist.CLI.Tests.Output;

public class ConsoleProgressReporterTests
{
    [Fact]
    public void ReportStageStart_WritesCorrectFormat()
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportStageStart("2", "Lexing");

        Assert.Equal($"Stage 2 - Lexing{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    public void ReportStageComplete_WritesDetailsAndElapsed()
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);
        var elapsed = TimeSpan.FromSeconds(1.234);
        var details = new List<string> { "100 tokens", "5 files" };

        reporter.ReportStageComplete("2", elapsed, details);

        var expected =
            $"    100 tokens{Environment.NewLine}" +
            $"    5 files{Environment.NewLine}" +
            $"    Took 1.23s{Environment.NewLine}" +
            Environment.NewLine;

        Assert.Equal(expected, writer.ToString());
    }

    [Fact]
    public void ReportBuildSummary_Succeeded_WritesCorrectFormat()
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);
        var totalElapsed = TimeSpan.FromSeconds(1.5);

        reporter.ReportBuildSummary(succeeded: true, errorCount: 0, warningCount: 0, totalElapsed);

        Assert.Equal(
            $"Build succeeded  0 error(s), 0 warning(s)  Total: 1.50s{Environment.NewLine}",
            writer.ToString());
    }

    [Fact]
    public void ReportBuildSummary_Failed_WritesCorrectFormat()
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);
        var totalElapsed = TimeSpan.FromSeconds(2.34);

        reporter.ReportBuildSummary(succeeded: false, errorCount: 3, warningCount: 1, totalElapsed);

        Assert.Equal(
            $"Build failed  3 error(s), 1 warning(s)  Total: 2.34s{Environment.NewLine}",
            writer.ToString());
    }
}
