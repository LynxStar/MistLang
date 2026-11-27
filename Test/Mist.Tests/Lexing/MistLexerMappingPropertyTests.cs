// unified-diagnostic-formatting Property 3, Requirements 2.1, 2.2.
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;

namespace Mist.Tests.Lexing;

public record DiagnosticEntryData(string Message, string FilePath, int Line, int Column)
{
    internal static SourceText SourceAt(string path, int line, int column)
    {
        var padding = new string(' ', column);
        var text = line == 1 ? padding : padding + new string('\n', line - 1) + padding;
        return new SourceText(path, text);
    }
}

public class MistLexerMappingPropertyTests
{
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistLexerMappingPropertyTests) })]
    [Trait("Tier", "2")]
    public bool MistLexerMapping_PreservesStructuredFields(DiagnosticEntryData entryData)
    {
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        stormDiagnostics.Add(entryData.Message, entryData.FilePath, entryData.Line, entryData.Column);

        var entry = stormDiagnostics.Entries[0];
        var source = DiagnosticEntryData.SourceAt(entryData.FilePath, entryData.Line, entryData.Column);
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

        if(diagnostic.Message != entryData.Message)
        {
            return false;
        }

        return true;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MistLexerMappingPropertyTests) })]
    [Trait("Tier", "1")]
    public bool MistLexerMapping_PositiveLineColumn_ProducesNonNullLocation(DiagnosticEntryData entryData)
    {
        var source = DiagnosticEntryData.SourceAt(entryData.FilePath, entryData.Line, entryData.Column);

        return StormEntryLocation.Of(source, entryData.FilePath, entryData.Line, entryData.Column) is SourcePoint;
    }

    [Property(MaxTest = 100)]
    [Trait("Tier", "1")]
    public bool MistLexerMapping_NullFilePath_FallsBackToSourcePath(PositiveInt line, PositiveInt column)
    {
        var sourcePath = "original-source.mist";

        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        stormDiagnostics.Add("Some error", null, line.Get, column.Get);

        var entry = stormDiagnostics.Entries[0];

        return StormEntryLocation.Of(DiagnosticEntryData.SourceAt(sourcePath, line.Get, column.Get), entry.FilePath, entry.Line, entry.Column).Path == sourcePath;
    }
}