using System.Diagnostics;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI.Output;

/// <summary>
/// Kept apart from the progress reporter so that no verbosity hides a diagnostic.
/// </summary>
public sealed class CompileFeedback : IResolutionDiagnosticListener
{
    private readonly ICompilationProgressReporter progress;
    private readonly TextWriter error;
    private readonly HashSet<Diagnostic> written = new(ReferenceEqualityComparer.Instance);

    public CompileFeedback(ICompilationProgressReporter progress, TextWriter error)
    {
        this.progress = progress;
        this.error = error;
    }

    public void Receive(Diagnostic diagnostic)
    {
        error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        _ = written.Add(diagnostic);
    }

    public int Run(
        Func<IResolutionDiagnosticListener, CompilerResult> compile,
        Stopwatch total,
        Func<CompilerResult, int> afterSuccess)
    {
        written.Clear();
        var result = compile(this);

        total.Stop();
        progress.ReportBuildSummary(
            result.Succeeded,
            result.Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            result.Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
            total.Elapsed);

        foreach(var diagnostic in result.Diagnostics)
        {
            if(!written.Remove(diagnostic))
            {
                error.WriteLine(DiagnosticFormatter.Format(diagnostic));
            }
        }

        written.Clear();

        return result.Succeeded ? afterSuccess(result) : 1;
    }
}