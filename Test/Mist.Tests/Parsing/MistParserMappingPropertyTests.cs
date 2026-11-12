// Feature: unified-diagnostic-formatting, Property 4: MistParser mapping preserves structured fields
// **Validates: Requirements 3.1, 3.2**
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Lexing;
using Mist.Tests.Lexing;

namespace Mist.Tests.Parsing;

public class MistParserMappingPropertyTests
{
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistParserMappingPropertyTests) })]
    public bool MistParserMapping_PreservesStructuredFields(DiagnosticEntryData entryData)
    {
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        stormDiagnostics.Add(entryData.Message, entryData.FilePath, entryData.Line, entryData.Column);

        var entry = stormDiagnostics.Entries[0];
        var source = SourceOf(entryData.FilePath, entryData.Line, entryData.Column);
        var bag = new DiagnosticBag();

        bag.ReportAt(ParserDiagnostics.ParseError, StormEntryLocation.Of(source, entry.FilePath, entry.Line, entry.Column), entry.Message);

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

        if(diagnostic.Message != entryData.Message)
        {
            return false;
        }

        return true;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistParserMappingPropertyTests) })]
    public bool MistParserMapping_PositiveLineColumn_ProducesNonNullLocation(DiagnosticEntryData entryData)
    {
        var source = SourceOf(entryData.FilePath, entryData.Line, entryData.Column);

        return StormEntryLocation.Of(source, entryData.FilePath, entryData.Line, entryData.Column) is SourcePoint;
    }

    [Property(MaxTest = 100)]
    public bool MistParserMapping_NullFilePath_FallsBackToSourcePath(PositiveInt line, PositiveInt column)
    {
        var sourcePath = "original-source.mist";

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