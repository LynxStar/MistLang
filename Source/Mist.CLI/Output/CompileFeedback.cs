using System.Diagnostics;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI.Output;

public sealed class CompileFeedback(ICompilationProgressReporter progress, DiagnosticOutput output) : IResolutionDiagnosticListener
{
    public void Receive(Diagnostic diagnostic)
    {
        output.Stream(diagnostic);
    }

    public int Run(
        Func<IResolutionDiagnosticListener, CompilerResult> compile,
        Stopwatch total,
        Func<CompilerResult, int> afterSuccess)
    {
        var result = compile(this);
        output.Collect(result.Diagnostics);
        var succeeded = result.Succeeded && !output.HasErrors;
        total.Stop();
        var counts = output.Counts;
        progress.ReportBuildSummary(succeeded, counts.Errors, counts.Warnings, total.Elapsed);
        output.Complete(succeeded, result.Native);
        return succeeded ? afterSuccess(result) : 1;
    }
}