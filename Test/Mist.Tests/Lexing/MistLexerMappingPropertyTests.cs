// Feature: unified-diagnostic-formatting, Property 3: MistLexer mapping preserves structured fields
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Lexing;

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
/// Column, and a path matching the entry's FilePath.
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
    /// Location.Start.Column == entry.Column, and a path equal to entry.FilePath.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistLexerMappingPropertyTests) })]
    public bool MistLexerMapping_PreservesStructuredFields(DiagnosticEntryData entryData)
    {
        // Create a Storm DiagnosticBag and add the entry
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        stormDiagnostics.Add(entryData.Message, entryData.FilePath, entryData.Line, entryData.Column);

        var entry = stormDiagnostics.Entries[0];
        var source = SourceOf(entryData.FilePath, entryData.Line, entryData.Column);
        var bag = new DiagnosticBag();

        bag.ReportAt(LexerDiagnostics.LexError, StormEntryLocation.Of(source, entry.FilePath, entry.Line, entry.Column), entry.Message);

        var diagnostic = Assert.Single(bag.ToImmutableArray());
        if(diagnostic.Location is not SourcePoint)
        {
            return false;
        }

        if(diagnostic.Position().Start.Line != entryData.Line)
        {
            return false;
        }

        if(diagnostic.Position().Start.Column != entryData.Column)
        {
            return false;
        }

        if(diagnostic.Location.Path != entryData.FilePath)
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
        var source = SourceOf(entryData.FilePath, entryData.Line, entryData.Column);

        return StormEntryLocation.Of(source, entryData.FilePath, entryData.Line, entryData.Column) is SourcePoint;
    }

    /// <summary>
    /// Property 3 (supplementary): the path falls back to the source path when
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

        return StormEntryLocation.Of(SourceOf(sourcePath, line.Get, column.Get), entry.FilePath, entry.Line, entry.Column).Path == sourcePath;
    }

    private static Mist.Common.Source.SourceText SourceOf(string path, int lines, int width)
    {
        return new Mist.Common.Source.SourceText(path, string.Join("\n", Enumerable.Repeat(new string(' ', width), lines)));
    }
}