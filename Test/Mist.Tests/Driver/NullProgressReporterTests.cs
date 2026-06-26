using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

public sealed class NullProgressReporterTests
{
    [Fact]
    public void AllMethods_DoNotThrow()
    {
        var reporter = NullProgressReporter.Instance;

        var ex = Record.Exception(() =>
        {
            reporter.ReportStageStart("1", "Loading");
            reporter.ReportStageComplete("2", TimeSpan.FromMilliseconds(42), new[] { "detail1", "detail2" });
            reporter.ReportBuildSummary(true, 0, 0, TimeSpan.FromSeconds(1.5));
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Instance_ReturnsSameReference()
    {
        var first = NullProgressReporter.Instance;
        var second = NullProgressReporter.Instance;

        Assert.Same(first, second);
    }
}