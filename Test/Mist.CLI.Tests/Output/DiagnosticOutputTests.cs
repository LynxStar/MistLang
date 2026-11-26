using System.Diagnostics;
using System.Text.Json;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
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
    public void FailedDocumentWriteEscapesAndRecoveryAddsOnlyTheHostFailureToStderr()
    {
        using var error = new StringWriter();
        var failure = new IOException("Diagnostic document write failed");
        var writeAttempts = 0;
        var output = new DiagnosticOutput(error, DiagnosticFormat.Json, documentFile: _ =>
        {
            writeAttempts++;
            throw failure;
        });
        var location = new Absent(AbsenceReason.Unavailable);
        var earlier = new DiagnosticBag(output.Operation);
        earlier.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "earlier warning", DiagnosticSeverity.Warning), location);
        output.Collect(earlier.Admitted);

        Assert.Same(failure, Assert.Throws<IOException>(() => output.Complete(true)));
        const string warning = "warning MIST9001: earlier warning";
        var earlierOutput = warning + Environment.NewLine;
        Assert.Equal(earlierOutput, error.ToString());

        var host = new DiagnosticBag(output.Operation);
        var details = new SequenceValue(SlotKind.Text,
            [new TextValue("System.IO.IOException"), new TextValue(failure.Message), new TextValue("")]);
        host.Admit(DiagnosticFactory.Create(HostDiagnostics.UnhandledException, location, PipelineStage.Driver, [details]));
        output.ReportHostFailure(host.Admitted[0]);
        Assert.Equal(earlierOutput, error.ToString());
        output.Complete(false, outcome: "internalFailure");

        Assert.Equal(1, writeAttempts);
        Assert.Collection(error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            line => Assert.Equal(warning, line),
            line =>
            {
                Assert.StartsWith("error MIST0423: Internal compiler error:", line);
                Assert.Contains(failure.Message, line);
            });
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
    public void DefaultTextWithDocumentSinkHidesWarningOnlyOnStderr()
    {
        using var error = new StringWriter();
        var documents = new List<byte[]>();
        var output = new DiagnosticOutput(error, minimum: DiagnosticSeverity.Error, documentFile: documents.Add);
        var bag = new DiagnosticBag(output.Operation);
        var location = new Absent(AbsenceReason.TestOrigin);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "filtered warning", DiagnosticSeverity.Warning), location);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9002", "build failure", DiagnosticSeverity.Error), location);
        output.Collect(bag.Admitted);

        output.Complete(false);

        Assert.Equal("error MIST9002: build failure" + Environment.NewLine, error.ToString());
        using var json = JsonDocument.Parse(Assert.Single(documents));
        var diagnostics = json.RootElement.GetProperty("diagnostics");
        Assert.Equal(2, diagnostics.GetArrayLength());
        var warning = Assert.Single(diagnostics.EnumerateArray(), d => d.GetProperty("code").GetString() == "MIST9001");
        var failure = Assert.Single(diagnostics.EnumerateArray(), d => d.GetProperty("code").GetString() == "MIST9002");
        Assert.False(warning.GetProperty("visible").GetBoolean());
        Assert.True(failure.GetProperty("visible").GetBoolean());
        var counts = json.RootElement.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("admitted").GetInt32());
        Assert.Equal(1, counts.GetProperty("warnings").GetInt32());
        Assert.Equal(1, counts.GetProperty("errors").GetInt32());
        Assert.Equal(1, counts.GetProperty("hidden").GetInt32());
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

    [Fact]
    public void JsonKeepsManifestWarningsAbsentFromTheCompilerResult()
    {
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error, DiagnosticFormat.Json);
        var manifest = new DiagnosticBag(output.Operation);
        manifest.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "manifest warning", DiagnosticSeverity.Warning), new FileOnly("mist.toml"));
        output.Collect(manifest.Admitted);

        var compiler = new DiagnosticBag(output.Operation);
        compiler.ReportAt(DiagnosticFixtures.Descriptor("MIST9002", "compiler warning", DiagnosticSeverity.Warning), new FileOnly("Program.mist"));
        output.CollectResult(compiler.Admitted);
        output.Complete(true);

        using var json = JsonDocument.Parse(error.ToString());
        var root = json.RootElement;
        Assert.Equal(["MIST9001", "MIST9002"], root.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()));
        Assert.Equal(2, root.GetProperty("counts").GetProperty("warnings").GetInt32());
    }

    [Fact]
    public void JsonKeepsFinalAppendOrderWhenADeferredDiagnosticHasAnEarlierOrdinal()
    {
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error, DiagnosticFormat.Json);
        var bag = new DiagnosticBag(output.Operation);
        var location = new Absent(AbsenceReason.TestOrigin);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "dependency failure"), location);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9002", "deferred failure"), location);
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9003", "binding failure"), location);
        var dependency = bag.Admitted[0];
        var deferred = bag.Admitted[1];
        var binding = bag.Admitted[2];

        output.Stream(dependency);
        output.CollectResult([dependency, binding, deferred]);
        output.Complete(false);

        using var json = JsonDocument.Parse(error.ToString());
        Assert.Equal([1, 3, 2], json.RootElement.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("ordinal").GetInt32()));
    }
}