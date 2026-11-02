using Mist.Driver.Contracts;

namespace SnippetEmitter;

internal sealed class SnippetTimings : ICompilationProgressReporter
{
    internal Dictionary<string, double> Stages { get; } = [];

    internal int Attempts { get; set; }

    internal void Clear()
    {
        Stages.Clear();
        Attempts = 0;
    }

    internal void Add(string stage, TimeSpan elapsed)
    {
        Stages[stage] = Stages.GetValueOrDefault(stage) + elapsed.TotalMilliseconds;
    }

    public void ReportStageStart(string stageId, string title)
    {
    }

    public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details)
    {
        Add(stageId, elapsed);
    }

    public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed)
    {
    }
}

internal sealed record SnippetResult(
    string Path, string? Emitted, string Errors, double Milliseconds, int Attempts, Dictionary<string, double> Stages);