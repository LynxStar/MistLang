using Mist.Driver.Contracts;

namespace Mist.Tests.Helpers;

internal sealed class RecordingProgressReporter : ICompilationProgressReporter
{
    public List<ProgressEvent> Events { get; } = [];

    public int Summaries { get; private set; }

    public IEnumerable<string> Sequence => Events.Select(progress => $"{progress.Kind} {progress.StageId}");

    public void ReportStageStart(string stageId, string title)
    {
        Events.Add(new ProgressEvent("start", stageId, title, []));
    }

    public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details)
    {
        Events.Add(new ProgressEvent("complete", stageId, null, details));
    }

    public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed)
    {
        Summaries++;
    }

    public IReadOnlyList<string> DetailsOf(string stageId)
    {
        return Events.Single(progress => progress.Kind == "complete" && progress.StageId == stageId).Details;
    }
}

internal sealed record ProgressEvent(string Kind, string StageId, string? Title, IReadOnlyList<string> Details);