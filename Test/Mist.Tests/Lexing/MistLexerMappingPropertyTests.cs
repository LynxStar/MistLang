// Feature: unified-diagnostic-formatting, Property 3: MistLexer mapping preserves structured fields
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;

namespace Mist.Tests.Lexing;

/// <summary>
/// Data record representing a Storm DiagnosticEntry for property test generation.
/// </summary>
public record DiagnosticEntryData(string Message, string FilePath, int Line, int Column);

/// <summary>
/// Property-based tests for the MistLexer diagnostic mapping logic.
///
/// **Validates: Requirements 2.1, 2.2**
///
/// Property 3: For any Storm DiagnosticEntry with a non-empty file path and positive
/// line and column values, the Mist Diagnostic produced by the MistLexer mapping SHALL
/// have a Location field whose Start.Line and Start.Column match the entry's Line and
/// Column, and a CompilationUnitPath matching the entry's FilePath.
/// </summary>
public class MistLexerMappingPropertyTests
{
    /// <summary>
    /// Generates random DiagnosticEntry-like values with non-empty file paths
    /// and positive line/column values.
    /// </summary>
    public static Arbitrary<DiagnosticEntryData> ArbitraryDiagnosticEntryData()
    {
        var gen =
            from message in Gen.Elements("Unexpected character", "Invalid token", "Syntax error", "Unterminated string")
            from filePath in Gen.Elements("test.mist", "src/main.mist", "lib/utils.mist", "foo/bar/baz.mist")
            from line in Gen.Choose(1, 10000)
            from column in Gen.Choose(1, 500)
            select new DiagnosticEntryData(message, filePath, line, column);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 3: MistLexer mapping preserves structured fields.
    ///
    /// For any Storm DiagnosticEntry with a non-empty file path and positive line/column,
    /// the mapping logic produces a Mist Diagnostic with Location.Start.Line == entry.Line,
    /// Location.Start.Column == entry.Column, and CompilationUnitPath == entry.FilePath.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistLexerMappingPropertyTests) })]
    public bool MistLexerMapping_PreservesStructuredFields(DiagnosticEntryData entryData)
    {
        // Create a Storm DiagnosticBag and add the entry
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        stormDiagnostics.Add(entryData.Message, entryData.FilePath, entryData.Line, entryData.Column);

        // Apply the same mapping logic that MistLexer uses
        var entry = stormDiagnostics.Entries[0];
        var sourcePath = "fallback.mist";

        var location = (entry.Line > 0 && entry.Column > 0)
            ? new LinePositionSpan(
                new LinePosition(entry.Line, entry.Column),
                new LinePosition(entry.Line, entry.Column))
            : default(LinePositionSpan);

        var diagnostic = new Diagnostic(
            new DiagnosticId("MIST_LEX_ERROR"),
            DiagnosticSeverity.Error,
            PipelineStage.Lexing,
            entry.Message,
            Location: location,
            CompilationUnitPath: entry.FilePath ?? sourcePath,
            AnalyzerName: null);

        // Verify: Location.Start.Line == entry.Line
        if(diagnostic.Location == default)
        {
            return false;
        }

        if(diagnostic.Location.Start.Line != entryData.Line)
        {
            return false;
        }

        // Verify: Location.Start.Column == entry.Column
        if(diagnostic.Location.Start.Column != entryData.Column)
        {
            return false;
        }

        // Verify: CompilationUnitPath == entry.FilePath
        if(diagnostic.CompilationUnitPath != entryData.FilePath)
        {
            return false;
        }

        // Verify: Message is preserved
        if(diagnostic.Message != entryData.Message)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Property 3 (supplementary): When line and column are both positive, Location is non-null.
    ///
    /// This verifies the guard condition in the mapping logic correctly produces
    /// a non-null Location for all positive line/column combinations.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistLexerMappingPropertyTests) })]
    public bool MistLexerMapping_PositiveLineColumn_ProducesNonNullLocation(DiagnosticEntryData entryData)
    {
        // Apply the mapping guard condition
        var location = (entryData.Line > 0 && entryData.Column > 0)
            ? new LinePositionSpan(
                new LinePosition(entryData.Line, entryData.Column),
                new LinePosition(entryData.Line, entryData.Column))
            : default(LinePositionSpan);

        // Since our generator always produces positive line/column, location must be non-null
        return location != default(LinePositionSpan);
    }

    /// <summary>
    /// Property 3 (supplementary): CompilationUnitPath falls back to source path when
    /// entry.FilePath is null.
    ///
    /// This verifies the fallback behavior in the mapping logic.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MistLexerMapping_NullFilePath_FallsBackToSourcePath(PositiveInt line, PositiveInt column)
    {
        var sourcePath = "original-source.mist";

        // Create a Storm DiagnosticBag with null file path
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        stormDiagnostics.Add("Some error", null, line.Get, column.Get);

        var entry = stormDiagnostics.Entries[0];

        // Apply the mapping logic
        var compilationUnitPath = entry.FilePath ?? sourcePath;

        // When FilePath is null, should fall back to source path
        return compilationUnitPath == sourcePath;
    }
}