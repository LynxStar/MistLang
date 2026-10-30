using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

// Native severity tokens belong to the provider; they do not extend the admitted Mist severity set.
public sealed record NativeDiagnostic(string Provider, string Code, string Severity, string Message, Location Location);

public sealed record DiagnosticDocument(
    string Compiler,
    string Command,
    string Project,
    string Outcome,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<NativeDiagnostic> Native,
    DiagnosticSeverity Minimum = DiagnosticSeverity.Info);

public sealed record DiagnosticCounts(int Admitted, int Errors, int Warnings, int Info, int Hidden)
{
    public static bool IsVisible(Diagnostic diagnostic, DiagnosticSeverity minimum)
    {
        return diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Severity >= minimum;
    }

    public static DiagnosticCounts From(IEnumerable<Diagnostic> diagnostics, DiagnosticSeverity minimum)
    {
        var admitted = 0;
        var errors = 0;
        var warnings = 0;
        var info = 0;
        var hidden = 0;
        foreach(var diagnostic in diagnostics)
        {
            admitted++;
            errors += diagnostic.Severity == DiagnosticSeverity.Error ? 1 : 0;
            warnings += diagnostic.Severity == DiagnosticSeverity.Warning ? 1 : 0;
            info += diagnostic.Severity == DiagnosticSeverity.Info ? 1 : 0;
            hidden += IsVisible(diagnostic, minimum) ? 0 : 1;
        }

        return new DiagnosticCounts(admitted, errors, warnings, info, hidden);
    }
}