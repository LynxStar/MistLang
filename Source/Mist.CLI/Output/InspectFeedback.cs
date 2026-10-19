using Mist.Common.Diagnostics;

namespace Mist.CLI.Output;

/// <summary>
/// Resolution diagnostics reach <c>mist inspect</c> as they reach a build: one line each, in the
/// <see cref="DiagnosticFormatter"/> text. An error withholds the report, which would describe assemblies the
/// project does not resolve.
/// </summary>
public static class InspectFeedback
{
    public static bool Report(IEnumerable<Diagnostic> diagnostics, TextWriter error)
    {
        var failed = false;

        foreach(var diagnostic in diagnostics)
        {
            error.WriteLine(DiagnosticFormatter.Format(diagnostic));
            failed |= diagnostic.Severity == DiagnosticSeverity.Error;
        }

        return failed;
    }
}