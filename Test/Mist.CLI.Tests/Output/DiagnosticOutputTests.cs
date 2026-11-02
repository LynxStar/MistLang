using System.Diagnostics;
using System.Text.Json;
using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests.Output;

[Trait("Tier", "2")]
public sealed class DiagnosticOutputTests
{
    [Fact]
    public void CopiesOfAnOrdinalAreWrittenOnceAndDistinctAdmissionsRemainDistinct()
    {
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error);
        var bag = new DiagnosticBag(output.Operation);
        var descriptor = DiagnosticFixtures.Descriptor("MIST9001", "same", DiagnosticSeverity.Warning);
        bag.ReportAt(descriptor, new Absent(AbsenceReason.TestOrigin));
        bag.ReportAt(descriptor, new Absent(AbsenceReason.TestOrigin));
        output.Stream(bag.Admitted[0]);
        output.Stream(bag.Admitted[0] with { });
        output.Collect(bag.Admitted);
        output.Complete(true);
        output.Complete(true);
        Assert.Equal(2, error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(2, output.Counts.Admitted);
    }

    [Fact]
    public void FilterDoesNotChangeSummaryOrFailureAndQuietDoesNotHideErrors()
    {
        using var error = new StringWriter();
        using var progress = new StringWriter();
        var output = new DiagnosticOutput(error, minimum: DiagnosticSeverity.Error);
        var bag = new DiagnosticBag(output.Operation);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "warning", DiagnosticSeverity.Warning), new Absent(AbsenceReason.TestOrigin));
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9002", "failure"), new Absent(AbsenceReason.TestOrigin));
        var exit = new CompileFeedback(new ConsoleProgressReporter(progress), output).Run(
            _ => new CompilerResult(true, "artifact", bag.ToImmutableArray()), Stopwatch.StartNew(), _ => throw new Exception("Must not launch"));
        Assert.Equal(1, exit);
        Assert.DoesNotContain("warning", error.ToString());
        Assert.Contains("error MIST9002", error.ToString());
        Assert.Contains("1 error(s), 1 warning(s)", progress.ToString());
    }

    [Fact]
    public void JsonBuffersStreamedFindingsUntilCompletion()
    {
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error, DiagnosticFormat.Json);
        var bag = new DiagnosticBag(output.Operation);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "warning", DiagnosticSeverity.Warning), new Absent(AbsenceReason.TestOrigin));
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9002", "second", DiagnosticSeverity.Warning), new Absent(AbsenceReason.TestOrigin));
        output.Stream(bag.Admitted[1]);
        Assert.Equal("", error.ToString());
        output.CollectResult(bag.Admitted);
        output.Complete(true);
        using var json = JsonDocument.Parse(error.ToString());
        Assert.Equal([1, 2], json.RootElement.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("ordinal").GetInt32()));
    }

    [Theory]
    [InlineData(false, "Counter.razor(1,4): error MIST9001: mapped")]
    [InlineData(true, "Counter.razor(1,4): error MIST9001: mapped (translated: out/Counter.razor.mist(7,9))")]
    public void TheTranslatedPositionIsWrittenOnlyForVerboseOutput(bool verbose, string expected)
    {
        using var error = new StringWriter();
        var output = new DiagnosticControls().CreateOutput(error, "build", ".", verbose);
        var bag = new DiagnosticBag(output.Operation);
        var mapped = new SourceRange(new SourceIdentity(new SourceText("Counter.razor", "<p>@count</p>")), 3, 6);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "mapped"),
            new GeneratedLocation("out/Counter.razor.mist", new(new(7, 9), new(7, 14)), mapped));
        output.Collect(bag.Admitted);
        output.Complete(false);
        Assert.Equal(expected + Environment.NewLine, error.ToString());
    }
}