using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Source;

namespace Mist.Tests.Lexing;

public record LocationTestInput(string Prefix, string Suffix);

/// <summary>
/// Property-based tests verifying that the line/column stored in a DiagnosticEntry
/// by the generated lexer matches the line/column computed by
/// <see cref="SourceText.ToLinePositionSpan"/> for the same character offset.
/// </summary>
public class StoredLocationPropertyTests
{
    /// <summary>
    /// Characters that are valid in Mist source and won't produce lexer errors on their own.
    /// </summary>
    private static readonly char[] SafeChars = "abcdefghijklmnopqrstuvwxyz 0123456789\n".ToCharArray();

    /// <summary>
    /// An invalid character that the Mist lexer cannot recognize, guaranteed to produce
    /// an error diagnostic. The tilde (~) is not part of any Mist token definition.
    /// </summary>
    private const char InvalidChar = '~';

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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StoredLocationPropertyTests) })]
    public bool StoredLocation_MatchesComputedPosition(LocationTestInput input)
    {
        var source = input.Prefix + InvalidChar + input.Suffix;
        var invalidCharOffset = input.Prefix.Length;
        var filePath = "test.mist";

        var stormSource = new Storm.Runtime.SourceText(filePath, source);
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        _ = Generated.MistLexer.Lex(stormSource, stormDiagnostics);

        if(!stormDiagnostics.HasErrors)
        {
            return false;
        }

        // The lexer may produce multiple diagnostics if the suffix also contains
        // characters that form partial tokens.
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

        var mistSourceText = new SourceText(filePath, source);
        var span = new SourceSpan(invalidCharOffset, 1);
        var computedPosition = mistSourceText.ToLinePositionSpan(span);

        if(diagnosticEntry.Line != computedPosition.Start.Line)
        {
            return false;
        }

        if(diagnosticEntry.Column != computedPosition.Start.Column)
        {
            return false;
        }

        return true;
    }
}