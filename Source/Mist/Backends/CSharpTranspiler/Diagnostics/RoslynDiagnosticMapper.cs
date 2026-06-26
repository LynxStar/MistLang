using System.Text.RegularExpressions;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Diagnostics;

/// <summary>
/// Parses MSBuild-format diagnostic lines and converts them to
/// <see cref="Diagnostic"/> instances.
/// Format: <c>file(line,col): severity code: message</c>
/// </summary>
public sealed partial class RoslynDiagnosticMapper
{
    // Pattern: file(line,col): severity code: message
    [GeneratedRegex(@"^(.+?)\((\d+),(\d+)\):\s+(warning|error)\s+(\w+):\s+(.+)$")]
    private static partial Regex DiagnosticLineRegex();

    public static Diagnostic? TryParse(string line)
    {
        var match = DiagnosticLineRegex().Match(line.Trim());
        if(!match.Success)
        {
            return null;
        }

        var filePath = match.Groups[1].Value;
        var lineNum = int.Parse(match.Groups[2].Value);
        var colNum = int.Parse(match.Groups[3].Value);
        var severityStr = match.Groups[4].Value;
        var code = match.Groups[5].Value;
        var message = match.Groups[6].Value;

        var severity = severityStr.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning;

        var location = new LinePositionSpan(
            new LinePosition(lineNum, colNum),
            new LinePosition(lineNum, colNum));

        return new Diagnostic(
            new DiagnosticId(code),
            severity,
            PipelineStage.Backend,
            message,
            location,
            CompilationUnitPath: filePath,
            AnalyzerName: null);
    }
}