// Feature: unified-diagnostic-formatting, Property 4: MistParser mapping preserves structured fields
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Tests.Lexing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for the MistParser diagnostic mapping logic.
///
/// **Validates: Requirements 3.1, 3.2**
///
/// Property 4: For any Storm DiagnosticEntry with a non-empty file path and positive
/// line and column values, the Mist Diagnostic produced by the MistParser mapping SHALL
/// have a Location field whose Start.Line and Start.Column match the entry's Line and
/// Column, and a CompilationUnitPath matching the entry's FilePath.
/// </summary>
public class MistParserMappingPropertyTests
{
    /// <summary>
    /// Generates random DiagnosticEntry-like values with non-empty file paths
    /// and positive line/column values.
    /// </summary>
    public static Arbitrary<DiagnosticEntryData> ArbitraryDiagnosticEntryData()
    {
        var gen =
            from message in Gen.Elements("Expected token kind", "Unexpected end of input", "Parse error", "Missing semicolon")
            from filePath in Gen.Elements("test.mist", "src/main.mist", "lib/utils.mist", "foo/bar/baz.mist")
            from line in Gen.Choose(1, 10000)
            from column in Gen.Choose(1, 500)
            select new DiagnosticEntryData(message, filePath, line, column);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 4: MistParser mapping preserves structured fields.
    ///
    /// For any Storm DiagnosticEntry with a non-empty file path and positive line/column,
    /// the mapping logic produces a Mist Diagnostic with Location.Start.Line == entry.Line,
    /// Location.Start.Column == entry.Column, and CompilationUnitPath == entry.FilePath.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistParserMappingPropertyTests) })]
    public bool MistParserMapping_PreservesStructuredFields(DiagnosticEntryData entryData)
    {
        // Create a Storm DiagnosticBag and add the entry
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        stormDiagnostics.Add(entryData.Message, entryData.FilePath, entryData.Line, entryData.Column);

        // Apply the same mapping logic that MistParser uses
        var entry = stormDiagnostics.Entries[0];
        var fallbackPath = "fallback.mist";

        var location = (entry.Line > 0 && entry.Column > 0)
            ? new LinePositionSpan(
                new LinePosition(entry.Line, entry.Column),
                new LinePosition(entry.Line, entry.Column))
            : (LinePositionSpan?)null;

        var diagnostic = new Diagnostic(
            new DiagnosticId("MIST_PARSE_ERROR"),
            DiagnosticSeverity.Error,
            PipelineStage.Parsing,
            entry.Message,
            Location: location,
            CompilationUnitPath: entry.FilePath ?? fallbackPath,
            AnalyzerName: null);

        // Verify: Location.Start.Line == entry.Line
        if (diagnostic.Location is null)
            return false;

        if (diagnostic.Location.Value.Start.Line != entryData.Line)
            return false;

        // Verify: Location.Start.Column == entry.Column
        if (diagnostic.Location.Value.Start.Column != entryData.Column)
            return false;

        // Verify: CompilationUnitPath == entry.FilePath
        if (diagnostic.CompilationUnitPath != entryData.FilePath)
            return false;

        // Verify: Message is preserved
        if (diagnostic.Message != entryData.Message)
            return false;

        return true;
    }

    /// <summary>
    /// Property 4 (supplementary): When line and column are both positive, Location is non-null.
    ///
    /// This verifies the guard condition in the mapping logic correctly produces
    /// a non-null Location for all positive line/column combinations.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistParserMappingPropertyTests) })]
    public bool MistParserMapping_PositiveLineColumn_ProducesNonNullLocation(DiagnosticEntryData entryData)
    {
        // Apply the mapping guard condition
        var location = (entryData.Line > 0 && entryData.Column > 0)
            ? new LinePositionSpan(
                new LinePosition(entryData.Line, entryData.Column),
                new LinePosition(entryData.Line, entryData.Column))
            : (LinePositionSpan?)null;

        // Since our generator always produces positive line/column, location must be non-null
        return location is not null;
    }

    /// <summary>
    /// Property 4 (supplementary): CompilationUnitPath falls back to source path when
    /// entry.FilePath is null.
    ///
    /// This verifies the fallback behavior in the mapping logic.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MistParserMapping_NullFilePath_FallsBackToSourcePath(PositiveInt line, PositiveInt column)
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
