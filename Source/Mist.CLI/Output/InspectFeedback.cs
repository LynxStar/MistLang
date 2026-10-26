using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Output;

/// <summary>
/// Resolution diagnostics reach <c>mist inspect</c> as they reach a build: one line each, in the
/// <see cref="DiagnosticFormatter"/> text. An error withholds the report, which would describe assemblies the
/// project does not resolve.
/// </summary>
public sealed class InspectFeedback(Action<Diagnostic> report)
{
    public InspectFeedback(TextWriter error) : this(diagnostic => error.WriteLine(DiagnosticFormatter.Format(diagnostic)))
    {
    }

    private int written;

    public bool Failed { get; private set; }

    public void Report(ImmutableArray<Diagnostic> reported)
    {
        for(; written < reported.Length; written++)
        {
            var diagnostic = reported[written];
            report(diagnostic);
            Failed |= diagnostic.Severity == DiagnosticSeverity.Error;
        }
    }
}