using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.CLI.Tests.Output;

/// <summary>
/// Case 1 carries a location and a path, 2 a path only, 3 neither.
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

public class DiagnosticFormatterPropertyTests
{
    private static readonly Regex Case1Pattern = new(
        @"^(?<file>.+)\((?<line>\d+),(?<col>\d+)\): (?<sev>error|warning) (?<id>[^:]+): (?<msg>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex Case2Pattern = new(
        @"^(?<file>.+): (?<sev>error|warning) (?<id>[^:]+): (?<msg>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex Case3Pattern = new(
        @"^(?<sev>error|warning) (?<id>[^:]+): (?<msg>.+)$",
        RegexOptions.Compiled);

    public static Arbitrary<FormatterTestInput> ArbitraryFormatterTestInput()
    {
        var gen =
            from id in Gen.Elements("MIST001", "MIST002", "MIST0120", "MIST0122", "TEST_UNDEFINED_TYPE")
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

        return DiagnosticFixtures.Occurrence(input.IdValue,
            input.Severity,
            input.Stage,
            input.Message,
            location: location,
            compilationUnitPath: compilationUnitPath,
            analyzerName: null);
    }

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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticFormatterPropertyTests) })]
    public bool FormatterOutput_PreservesIdAndMessage(FormatterTestInput input)
    {
        var diagnostic = BuildDiagnostic(input);
        var formatted = DiagnosticFormatter.Format(diagnostic);

        return formatted.Contains(input.IdValue) && formatted.Contains(input.Message);
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticFormatterPropertyTests) })]
    public bool FormatterOutput_Case1_ContainsFileAndPosition(FormatterTestInput input)
    {
        if(input.Case != 1)
        {
            return true;
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