// Feature: unified-diagnostic-formatting, Property 2: Formatter output format
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.CLI.Tests.Output;

/// <summary>
/// Data record representing a generated Diagnostic for property test generation.
/// The Case field determines which format variant to test:
///   1 = Location + Path, 2 = Path only, 3 = Neither.
/// </summary>
public record FormatterTestInput(
    string IdValue,
    DiagnosticSeverity Severity,
    PipelineStage Stage,
    string Message,
    int Case,
    string? FilePath,
    int Line,
    int Column);

/// <summary>
/// Property-based tests for the DiagnosticFormatter output format.
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
///
/// Property 2: For any Diagnostic value, the string produced by DiagnosticFormatter.Format()
/// SHALL match the expected pattern depending on the presence of Location and CompilationUnitPath.
/// The severity token SHALL be the lowercase string "error" or "warning".
/// </summary>
public class DiagnosticFormatterPropertyTests
{
    // Regex for Case 1: file(line,col): severity id: message
    private static readonly Regex Case1Pattern = new(
        @"^(?<file>.+)\((?<line>\d+),(?<col>\d+)\): (?<sev>error|warning) (?<id>[^:]+): (?<msg>.+)$",
        RegexOptions.Compiled);

    // Regex for Case 2: file: severity id: message
    private static readonly Regex Case2Pattern = new(
        @"^(?<file>.+): (?<sev>error|warning) (?<id>[^:]+): (?<msg>.+)$",
        RegexOptions.Compiled);

    // Regex for Case 3: severity id: message
    private static readonly Regex Case3Pattern = new(
        @"^(?<sev>error|warning) (?<id>[^:]+): (?<msg>.+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Generates random FormatterTestInput values covering all three diagnostic cases.
    /// </summary>
    public static Arbitrary<FormatterTestInput> ArbitraryFormatterTestInput()
    {
        var gen =
            from id in Gen.Elements("MIST001", "MIST002", "MIST_LEX_ERROR", "MIST_PARSE_ERROR", "MIST_UNDEFINED_TYPE")
            from severity in Gen.Elements(DiagnosticSeverity.Error, DiagnosticSeverity.Warning)
            from stage in Gen.Elements(
                PipelineStage.Lexing, PipelineStage.Parsing,
                PipelineStage.SemanticModelConstruction, PipelineStage.Analysis,
                PipelineStage.Backend, PipelineStage.Driver)
            from message in Gen.Elements(
                "Unexpected token", "Undefined type", "Unused variable",
                "Cannot inherit from struct", "Circular dependency detected")
            from caseNum in Gen.Elements(1, 2, 3)
            from filePath in Gen.Elements("test.mist", "src/main.mist", "lib/utils.mist", "foo/bar/baz.mist")
            from line in Gen.Choose(1, 10000)
            from column in Gen.Choose(1, 500)
            select new FormatterTestInput(id, severity, stage, message, caseNum, filePath, line, column);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Builds a Diagnostic from the test input according to the case number.
    /// </summary>
    private static Diagnostic BuildDiagnostic(FormatterTestInput input)
    {
        var location = input.Case == 1
            ? new LinePositionSpan(
                new LinePosition(input.Line, input.Column),
                new LinePosition(input.Line, input.Column))
            : default;

        var compilationUnitPath = input.Case switch
        {
            1 => input.FilePath ?? "<unknown>",
            2 => input.FilePath ?? "<unknown>",
            3 => "<unknown>",
            _ => "<unknown>",
        };

        return new Diagnostic(
            new DiagnosticId(input.IdValue),
            input.Severity,
            input.Stage,
            input.Message,
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: null);
    }

    /// <summary>
    /// Property 2: Formatter output matches expected pattern for each case.
    ///
    /// For any Diagnostic value, the formatted string matches the expected regex pattern
    /// based on whether Location and CompilationUnitPath are present.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticFormatterPropertyTests) })]
    public bool FormatterOutput_MatchesExpectedPattern(FormatterTestInput input)
    {
        var diagnostic = BuildDiagnostic(input);
        var formatted = DiagnosticFormatter.Format(diagnostic);

        return input.Case switch
        {
            1 => Case1Pattern.IsMatch(formatted),
            2 => Case2Pattern.IsMatch(formatted),
            3 => Case3Pattern.IsMatch(formatted),
            _ => false,
        };
    }

    /// <summary>
    /// Property 2 (supplementary): Severity token is always lowercase "error" or "warning".
    ///
    /// For any Diagnostic value, the formatted output contains the severity as
    /// exactly "error" or "warning" in lowercase.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticFormatterPropertyTests) })]
    public bool FormatterOutput_SeverityIsLowercase(FormatterTestInput input)
    {
        var diagnostic = BuildDiagnostic(input);
        var formatted = DiagnosticFormatter.Format(diagnostic);

        var expectedSeverity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        };

        return formatted.Contains($"{expectedSeverity} {input.IdValue}:");
    }

    /// <summary>
    /// Property 2 (supplementary): Formatted output preserves the diagnostic id and message.
    ///
    /// For any Diagnostic value, the formatted string contains the diagnostic id value
    /// and the original message text.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticFormatterPropertyTests) })]
    public bool FormatterOutput_PreservesIdAndMessage(FormatterTestInput input)
    {
        var diagnostic = BuildDiagnostic(input);
        var formatted = DiagnosticFormatter.Format(diagnostic);

        // The formatted string must contain the id and message
        return formatted.Contains(input.IdValue) && formatted.Contains(input.Message);
    }

    /// <summary>
    /// Property 2 (supplementary): Case 1 output contains the correct file, line, and column.
    ///
    /// When both Location and CompilationUnitPath are present, the formatted string
    /// includes the file path and the line/column in parentheses.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticFormatterPropertyTests) })]
    public bool FormatterOutput_Case1_ContainsFileAndPosition(FormatterTestInput input)
    {
        // Only test Case 1 inputs
        if(input.Case != 1)
        {
            return true; // skip non-Case-1 inputs (vacuously true)
        }

        var diagnostic = BuildDiagnostic(input);
        var formatted = DiagnosticFormatter.Format(diagnostic);

        var match = Case1Pattern.Match(formatted);
        if(!match.Success)
        {
            return false;
        }

        return match.Groups["file"].Value == input.FilePath
            && match.Groups["line"].Value == input.Line.ToString()
            && match.Groups["col"].Value == input.Column.ToString()
            && match.Groups["id"].Value == input.IdValue
            && match.Groups["msg"].Value == input.Message;
    }
}