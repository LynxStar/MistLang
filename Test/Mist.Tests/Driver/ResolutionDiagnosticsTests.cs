using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class ResolutionDiagnosticsTests
{
    [Fact]
    public void AWarningJoinsASucceededResultAndLeavesItsVerdictAndArtifactAlone()
    {
        var built = new CompilerResult(true, "bin/Probe.dll", []);

        var joined = Reporting(0, null, Warning("MIST0036")).JoinInto(built);

        Assert.True(joined.Succeeded);
        Assert.Equal("bin/Probe.dll", joined.PrimaryOutputArtifactPath);
        Assert.Equal(["MIST0036"], Ids(joined));
    }

    [Fact]
    public void AnErrorJoinsTheFailedResultThePipelineGaveItAndKeepsBothErrors()
    {
        var refused = new CompilerResult(false, null, [Error("MIST0298")]);

        var joined = Reporting(0, null, Error("MIST0036")).JoinInto(refused);

        Assert.False(joined.Succeeded);
        Assert.Null(joined.PrimaryOutputArtifactPath);
        Assert.Equal(["MIST0036", "MIST0298"], Ids(joined));
    }

    [Fact]
    public void TheyGoBackAfterWhatPrecededThemAndBeforeWhatFollowed()
    {
        var result = new CompilerResult(false, null, [Error("MIST0101"), Error("TEST_LEX"), Error("TEST_PARSE")]);

        var joined = Reporting(1, null, Error("MIST0036"), Error("MIST0035")).JoinInto(result);

        Assert.Equal(["MIST0101", "MIST0036", "MIST0035", "TEST_LEX", "TEST_PARSE"], Ids(joined));
    }

    [Fact]
    public void AResultShorterThanWhatPrecededThemTakesThemLast()
    {
        var result = new CompilerResult(false, null, [Error("MIST0101")]);

        var joined = Reporting(3, null, Error("MIST0036")).JoinInto(result);

        Assert.Equal(["MIST0101", "MIST0036"], Ids(joined));
    }

    [Fact]
    public void EachDiagnosticReachesTheListenerApart_InReportOrder()
    {
        var listener = new RecordingListener();
        var first = Error("MIST0036", "Package 'A'");
        var second = Error("MIST0036", "Package 'B'");
        var framework = Error("MIST0035", "Framework 'C'");

        Reporting(0, listener, first, second, framework).Deliver();

        Assert.Equal(3, listener.Received.Count);
        Assert.Same(first, listener.Received[0]);
        Assert.Same(second, listener.Received[1]);
        Assert.Same(framework, listener.Received[2]);
    }

    [Fact]
    public void TheResultKeepsTheInstancesTheListenerReceived_InStageOrder()
    {
        var listener = new RecordingListener();
        var loading = Error("MIST0101");
        var parsing = Error("TEST_PARSE");
        var resolution = Reporting(1, listener, Error("MIST0036", "Package 'A'"), Error("MIST0036", "Package 'B'"));
        resolution.Deliver();

        var joined = resolution.JoinInto(new CompilerResult(false, null, [loading, parsing]));

        Assert.Equal(4, joined.Diagnostics.Length);
        Assert.Same(loading, joined.Diagnostics[0]);
        Assert.Same(listener.Received[0], joined.Diagnostics[1]);
        Assert.Same(listener.Received[1], joined.Diagnostics[2]);
        Assert.Same(parsing, joined.Diagnostics[3]);
    }

    [Fact]
    public void ALaterDeliveryPassesOnOnlyWhatWasReportedSince()
    {
        var listener = new RecordingListener();
        var early = Error("MIST0036", "Package 'A'");
        var late = Error("MIST0035", "Framework 'C'");
        var resolution = Reporting(0, listener, early);

        resolution.Deliver();
        resolution.Reported.Add(late);
        resolution.Deliver();
        resolution.Deliver();

        Assert.Equal(2, listener.Received.Count);
        Assert.Same(early, listener.Received[0]);
        Assert.Same(late, listener.Received[1]);
    }

    [Fact]
    public void NothingReportedReachesTheListenerNever()
    {
        var listener = new RecordingListener();

        new ResolutionDiagnostics(0, listener).Deliver();

        Assert.Empty(listener.Received);
    }

    [Fact]
    public void WithNoListenerDeliveryChangesNothingTheResultReads()
    {
        var resolution = Reporting(0, null, Error("MIST0036"));
        resolution.Deliver();

        var joined = resolution.JoinInto(new CompilerResult(false, null, []));

        Assert.Equal(["MIST0036"], Ids(joined));
    }

    private static ResolutionDiagnostics Reporting(int preceding, IResolutionDiagnosticListener? listener, params Diagnostic[] reported)
    {
        var resolution = new ResolutionDiagnostics(preceding, listener);
        foreach(var diagnostic in reported)
        {
            resolution.Reported.Add(diagnostic);
        }

        return resolution;
    }

    private static IEnumerable<string> Ids(CompilerResult result)
    {
        return result.Diagnostics.Select(diagnostic => diagnostic.Number.Value);
    }

    private static Diagnostic Error(string id, string message = "probe")
    {
        return DiagnosticFixtures.Occurrence(id, DiagnosticSeverity.Error, PipelineStage.Driver, message, default, "probe.mist", null);
    }

    private static Diagnostic Warning(string id, string message = "probe")
    {
        return DiagnosticFixtures.Occurrence(id, DiagnosticSeverity.Warning, PipelineStage.Driver, message, default, "probe.mist", null);
    }

    private sealed class RecordingListener : IResolutionDiagnosticListener
    {
        public List<Diagnostic> Received { get; } = [];

        public void Receive(Diagnostic diagnostic)
        {
            Received.Add(diagnostic);
        }
    }
}