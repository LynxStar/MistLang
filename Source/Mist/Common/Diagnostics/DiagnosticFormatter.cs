namespace Mist.Common.Diagnostics;

/// <summary>
/// Formats diagnostics in MSBuild-compatible format for stderr output.
/// Format: <c>path(startLine,startCol): severity id: message</c>
/// </summary>
public static class DiagnosticFormatter
{
    public static string Format(Diagnostic diagnostic)
    {
        var severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        };

        return diagnostic.Location is { } location && diagnostic.CompilationUnitPath is not null
            ? $"{diagnostic.CompilationUnitPath}({location.Start.Line},{location.Start.Column}): {severity} {diagnostic.Id.Value}: {diagnostic.Message}"
            : diagnostic.CompilationUnitPath is not null
                ? $"{diagnostic.CompilationUnitPath}: {severity} {diagnostic.Id.Value}: {diagnostic.Message}"
                : $"{severity} {diagnostic.Id.Value}: {diagnostic.Message}"
                ;
    }
}
