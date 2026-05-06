namespace Mist.Driver.Contracts;

/// <summary>
/// No-op implementation of <see cref="ICompilationProgressReporter"/>.
/// Used in tests and any context where progress output is unwanted.
/// </summary>
public sealed class NullProgressReporter : ICompilationProgressReporter
{
    public static readonly NullProgressReporter Instance = new();

    private NullProgressReporter() { }

    public void ReportStageStart(int stageNumber, string title) { }
    public void ReportStageComplete(int stageNumber, TimeSpan elapsed, IReadOnlyList<string> details) { }
    public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed) { }
}
