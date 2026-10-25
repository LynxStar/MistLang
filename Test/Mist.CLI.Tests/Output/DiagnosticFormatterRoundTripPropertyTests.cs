// Feature: unified-diagnostic-formatting, Property 1: Formatter round-trip
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler.Diagnostics;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.CLI.Tests.Output;

/// <summary>
/// Data record for generating round-trip test inputs.
/// All fields are constrained so that Format() produces a string parseable by TryParse().
/// </summary>
public record RoundTripTestInput(
    string IdValue,
    DiagnosticSeverity Severity,
    PipelineStage Stage,
    string Message,
    string FilePath,
    int Line,
    int Column);

/// <summary>
/// Property-based tests for the DiagnosticFormatter ↔ RoslynDiagnosticMapper round-trip.
///
/// **Validates: Requirements 5.6**
///
/// Property 1: For any Diagnostic value with a non-null Location and CompilationUnitPath,
/// formatting with DiagnosticFormatter.Format() and parsing with RoslynDiagnosticMapper.TryParse()
/// SHALL recover the original severity, diagnostic id, and message.
/// </summary>
public class DiagnosticFormatterRoundTripPropertyTests
{
    /// <summary>
    /// Generates random RoundTripTestInput values with constraints that ensure
    /// the formatted string is parseable by RoslynDiagnosticMapper.TryParse():
    /// - IdValue contains only word characters (\w+)
    /// - FilePath does not contain patterns that confuse the regex
    /// - Line and Column are positive integers
    /// - Message is non-empty and single-line
    /// </summary>
    public static Arbitrary<RoundTripTestInput> ArbitraryRoundTripTestInput()
    {
        var gen =
            from id in Gen.Elements(
                "MIST001", "MIST002", "CS0001", "CS1234",
                "MIST_LEX_ERROR", "MIST_PARSE_ERROR", "MIST_UNDEFINED_TYPE")
            from severity in Gen.Elements(DiagnosticSeverity.Error, DiagnosticSeverity.Warning)
            from stage in Gen.Elements(
                PipelineStage.Lexing, PipelineStage.Parsing,
                PipelineStage.SemanticModelConstruction, PipelineStage.Analysis,
                PipelineStage.Backend, PipelineStage.Driver)
            from message in Gen.Elements(
                "Unexpected token", "Undefined type 'Foo'", "Unused variable 'x'",
                "Cannot inherit from struct", "Circular dependency detected",
                "Missing semicolon", "Type mismatch in assignment")
            from filePath in Gen.Elements(
                "test.mist", "src/main.mist", "lib/utils.mist",
                "foo/bar/baz.mist", "Program.cs", "Models/User.cs")
            from line in Gen.Choose(1, 10000)
            from column in Gen.Choose(1, 500)
            select new RoundTripTestInput(id, severity, stage, message, filePath, line, column);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Builds a Diagnostic with non-null Location and CompilationUnitPath from the test input.
    /// </summary>
    private static Diagnostic BuildDiagnostic(RoundTripTestInput input)
    {
        var location = new LinePositionSpan(
            new LinePosition(input.Line, input.Column),
            new LinePosition(input.Line, input.Column));

        return DiagnosticFixtures.Occurrence(input.IdValue,
            input.Severity,
            input.Stage,
            input.Message,
            location: location,
            compilationUnitPath: input.FilePath,
            analyzerName: null);
    }

    /// <summary>
    /// Property 1: Formatter round-trip recovers severity, diagnostic id, and message.
    ///
    /// For any Diagnostic with non-null Location and CompilationUnitPath,
    /// Format() → TryParse() recovers the original severity, diagnostic id, and message.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticFormatterRoundTripPropertyTests) })]
    public bool FormatterRoundTrip_RecoversSeverityIdAndMessage(RoundTripTestInput input)
    {
        var original = BuildDiagnostic(input);
        var formatted = DiagnosticFormatter.Format(original);
        var parsed = RoslynDiagnosticMapper.TryParse(formatted);

        if(parsed is null)
        {
            return false;
        }

        return parsed.Native is { } native
            && native.NativeSeverity == original.Severity
            && native.Code == original.Number.Value
            && parsed.Message == original.Message;
    }
}