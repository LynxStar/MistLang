#pragma warning disable IDE0130 // Namespace intentionally matches interface location, not folder structure
namespace Mist.Driver.Contracts;
#pragma warning restore IDE0130

/// <summary>
/// No-op implementation of <see cref="ICompilationProgressReporter"/>.
/// Used in tests and any context where progress output is unwanted.
/// </summary>
#pragma warning disable IDE0060 // Interface implementation — parameters required by contract but intentionally unused
public sealed class NullProgressReporter : ICompilationProgressReporter
{
    public static readonly NullProgressReporter Instance = new();

    private NullProgressReporter() { }

    public void ReportStageStart(string stageId, string title) { }
    public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details) { }
    public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed) { }
}
#pragma warning restore IDE0060