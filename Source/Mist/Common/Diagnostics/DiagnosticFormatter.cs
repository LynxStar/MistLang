namespace Mist.Common.Diagnostics;

/// Stderr diagnostics use MSBuild-compatible syntax.
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

        var location = diagnostic.Location;

        return location switch
        {
            SourceRange or SourcePoint when location.Lines is { } lines =>
                $"{location.Path}({lines.Start.Line},{lines.Start.Column}): {severity} {diagnostic.Number.Value}: {diagnostic.Message}",
            FileOnly or ProjectLocation or GeneratedLocation =>
                $"{location.Path}: {severity} {diagnostic.Number.Value}: {diagnostic.Message}",
            _ => $"{severity} {diagnostic.Number.Value}: {diagnostic.Message}",
        };
    }
}