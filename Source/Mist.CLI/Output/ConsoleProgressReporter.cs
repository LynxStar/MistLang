using Mist.Driver.Contracts;

namespace Mist.CLI.Output;

/// <summary>
/// Writes human-readable compilation progress to a <see cref="TextWriter"/>.
/// Accepts a <see cref="TextWriter"/> in the constructor for testability.
/// </summary>
public sealed class ConsoleProgressReporter : ICompilationProgressReporter
{
    private readonly TextWriter _out;

    public ConsoleProgressReporter(TextWriter output)
    {
        _out = output;
    }

    public void ReportStageStart(string stageId, string title)
    {
        _out.WriteLine($"Stage {stageId} - {title}");
    }

    public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details)
    {
        foreach (var detail in details)
            _out.WriteLine($"    {detail}");
        _out.WriteLine($"    Took {elapsed.TotalSeconds:F2}s");
        _out.WriteLine(); // blank separator line
    }

    public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed)
    {
        var status = succeeded ? "Build succeeded" : "Build failed";
        _out.WriteLine($"{status}  {errorCount} error(s), {warningCount} warning(s)  Total: {totalElapsed.TotalSeconds:F2}s");
    }
}
