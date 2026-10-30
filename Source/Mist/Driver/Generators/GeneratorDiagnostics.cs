using System.Text.RegularExpressions;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Driver.Generators;

internal static partial class GeneratorDiagnostics
{
    [GeneratedRegex(@"^(.*)\((\d+),(\d+)\): (error|warning) ([^: ]+): (.*)$")]
    private static partial Regex LinePattern();

    public static GeneratorMessage? Parse(string line)
    {
        var match = LinePattern().Match(line);
        if(!match.Success || !int.TryParse(match.Groups[2].Value, out var row) || row <= 0
            || !int.TryParse(match.Groups[3].Value, out var column) || column <= 0)
        {
            return null;
        }

        return new GeneratorMessage(match.Groups[1].Value, row, column, match.Groups[4].Value, match.Groups[5].Value, match.Groups[6].Value);
    }

    public static void Report(GeneratorMessage message, string generator, string staging, DiagnosticBag diagnostics)
    {
        var error = message.Severity == "error";
        var position = new LinePosition(message.Line, message.Column);
        var location = new GeneratedLocation(Path.GetFullPath(message.File, staging), new LinePositionSpan(position, position), null);
        diagnostics.ReportTyped(error ? DriverDiagnostics.GeneratorReportedError : DriverDiagnostics.GeneratorReportedWarning,
            location, new ProviderCode(generator, message.Code, error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning), new TextValue(message.Text));
    }
}