namespace Mist.Driver.Contracts;

public interface ICompilationProgressReporter
{
    void ReportStageStart(string stageId, string title);

    /// <param name="details">One or more detail lines (without leading indent — the reporter adds formatting).</param>
    void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details);

    /// <summary>Called once, at the end of the build.</summary>
    void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed);
}