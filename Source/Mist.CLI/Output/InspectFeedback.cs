using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Output;

/// An error withholds inspection because unresolved assemblies would make its report misleading.
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