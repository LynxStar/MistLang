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
            DiagnosticSeverity.Info => "info",
            _ => "info",
        };

        var hasLocation = diagnostic.Location != default;
        var hasPath = diagnostic.CompilationUnitPath is not ("" or "<unknown>");

        return hasLocation && hasPath
            ? $"{diagnostic.CompilationUnitPath}({diagnostic.Location.Start.Line},{diagnostic.Location.Start.Column}): {severity} {diagnostic.Id.Value}: {diagnostic.Message}"
            : hasPath
                ? $"{diagnostic.CompilationUnitPath}: {severity} {diagnostic.Id.Value}: {diagnostic.Message}"
                : $"{severity} {diagnostic.Id.Value}: {diagnostic.Message}"
                ;
    }
}