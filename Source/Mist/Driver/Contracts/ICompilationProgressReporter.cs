namespace Mist.Driver.Contracts;

/// <summary>
/// Receives progress notifications from the compilation pipeline.
/// </summary>
public interface ICompilationProgressReporter
{
    /// <summary>
    /// Called when a compilation stage begins.
    /// </summary>
    /// <param name="stageNumber">Zero-based stage index (0–6).</param>
    /// <param name="title">Human-readable stage name (e.g., "Cleaning", "Lexing").</param>
    void ReportStageStart(int stageNumber, string title);

    /// <summary>
    /// Called when a compilation stage completes.
    /// </summary>
    /// <param name="stageNumber">Zero-based stage index.</param>
    /// <param name="elapsed">Wall-clock time for this stage.</param>
    /// <param name="details">One or more detail lines (without leading indent — the reporter adds formatting).</param>
    void ReportStageComplete(int stageNumber, TimeSpan elapsed, IReadOnlyList<string> details);

    /// <summary>
    /// Called once at the end of the build to report the final outcome.
    /// </summary>
    void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed);
}
