// Feature: unified-diagnostic-formatting, Property 7: Stored diagnostic location matches computed position
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Source;

namespace Mist.Tests.Lexing;

/// <summary>
/// Test input: a source text string and a valid character offset where an invalid
/// character will be placed to trigger a lexer diagnostic.
/// </summary>
public record LocationTestInput(string Prefix, string Suffix);

/// <summary>
/// Property-based tests verifying that the line/column stored in a DiagnosticEntry
/// by the generated lexer matches the line/column computed by
/// <see cref="SourceText.ToLinePositionSpan"/> for the same character offset.
///
/// **Validates: Requirements 1.5**
///
/// Property 7: For any source text and any valid character offset within that text,
/// the 1-based line and column stored in a DiagnosticEntry by the generated lexer
/// SHALL equal the line and column computed by SourceText.ToLinePositionSpan()
/// for a span starting at that offset.
/// </summary>
public class StoredLocationPropertyTests
{
    /// <summary>
    /// Characters that are valid in Mist source and won't produce lexer errors on their own.
    /// We use simple identifiers, whitespace, and newlines to build the prefix/suffix.
    /// </summary>
    private static readonly char[] SafeChars = "abcdefghijklmnopqrstuvwxyz 0123456789\n".ToCharArray();

    /// <summary>
    /// An invalid character that the Mist lexer cannot recognize, guaranteed to produce
    /// an error diagnostic. The tilde (~) is not part of any Mist token definition.
    /// </summary>
    private const char InvalidChar = '~';

    /// <summary>
    /// Generates random prefix and suffix strings composed of safe Mist characters
    /// (letters, digits, spaces, newlines) to surround the invalid character.
    /// </summary>
    public static Arbitrary<LocationTestInput> ArbitraryLocationTestInput()
    {
        var safeCharGen = Gen.Elements(SafeChars);

        var gen =
            from prefixLen in Gen.Choose(0, 200)
            from prefixChars in Gen.ArrayOf(safeCharGen, prefixLen)
            from suffixLen in Gen.Choose(0, 50)
            from suffixChars in Gen.ArrayOf(safeCharGen, suffixLen)
            select new LocationTestInput(new string(prefixChars), new string(suffixChars));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 7: Stored diagnostic location matches computed position.
    ///
    /// Constructs source text with a single invalid character at a known offset,
    /// lexes it with the generated Mist lexer, and verifies the DiagnosticEntry's
    /// line/column matches what SourceText.ToLinePositionSpan() computes for
    /// a span at that offset.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StoredLocationPropertyTests) })]
    public bool StoredLocation_MatchesComputedPosition(LocationTestInput input)
    {
        // Build source: prefix + invalid char + suffix
        var source = input.Prefix + InvalidChar + input.Suffix;
        var invalidCharOffset = input.Prefix.Length;
        var filePath = "test.mist";

        // Lex with the generated Mist lexer (Storm runtime)
        var stormSource = new Storm.Runtime.SourceText(filePath, source);
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        _ = Generated.MistLexer.Lex(stormSource, stormDiagnostics);

        // The lexer should have produced at least one diagnostic for the invalid character
        if(!stormDiagnostics.HasErrors)
        {
            return false;
        }

        // Find the diagnostic entry for our invalid character.
        // The lexer may produce multiple diagnostics if the suffix also contains
        // characters that form partial tokens. We look for the first diagnostic
        // whose message mentions the invalid character.
        Storm.Runtime.DiagnosticEntry? targetEntry = null;
        foreach(var entry in stormDiagnostics.Entries)
        {
            if(entry.Message.Contains(InvalidChar))
            {
                targetEntry = entry;
                break;
            }
        }

        if(targetEntry is null)
        {
            return false;
        }

        var diagnosticEntry = targetEntry.Value;

        // Independently compute the expected line/column using Mist's SourceText
        var mistSourceText = new SourceText(filePath, source);
        var span = new SourceSpan(invalidCharOffset, 1);
        var computedPosition = mistSourceText.ToLinePositionSpan(span);

        // Verify: stored line matches computed line
        if(diagnosticEntry.Line != computedPosition.Start.Line)
        {
            return false;
        }

        // Verify: stored column matches computed column
        if(diagnosticEntry.Column != computedPosition.Start.Column)
        {
            return false;
        }

        return true;
    }
}