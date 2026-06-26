// Feature: binary-literals, Property 1: Valid binary literals tokenize as BINARY_LITERAL
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

/// <summary>
/// Property-based tests for binary literal tokenization.
///
/// **Validates: Requirements 1.1, 1.3, 1.5**
///
/// Property 1: For any valid binary literal string (0b/0B prefix, one or more binary
/// digits optionally separated by single underscores between digits, optional INT_SUFFIX),
/// the lexer SHALL produce a single BINARY_LITERAL token whose lexeme equals the input string.
/// </summary>
public class BinaryLiteralTokenPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();

    /// <summary>
    /// All valid INT_SUFFIX values, plus empty string for no suffix.
    /// </summary>
    private static readonly string[] Suffixes =
        ["", "l", "L", "u", "U", "UL", "ul", "Ul", "uL", "Lu", "lU", "LU", "lu"];

    /// <summary>
    /// Generates a random valid binary literal string following the grammar:
    /// "0" [bB] BINARY_DIGIT+ ("_" BINARY_DIGIT+)* INT_SUFFIX?
    /// </summary>
    public static Arbitrary<string> ArbitraryBinaryLiteral()
    {
        var gen =
            from prefix in Gen.Elements("0b", "0B")
            from firstGroup in GenBinaryDigitGroup()
            from separatorGroups in GenSeparatorGroups()
            from suffix in Gen.Elements(Suffixes)
            select prefix + firstGroup + separatorGroups + suffix;

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a group of 1–8 binary digits (e.g., "1010", "0", "11110000").
    /// </summary>
    private static Gen<string> GenBinaryDigitGroup()
    {
        return
            from length in Gen.Choose(1, 8)
            from digits in Gen.ArrayOf(Gen.Elements('0', '1'), length)
            select new string(digits);
    }

    /// <summary>
    /// Generates zero or more "_" + digit-group segments (e.g., "_0000_1111").
    /// </summary>
    private static Gen<string> GenSeparatorGroups()
    {
        return
            from count in Gen.Choose(0, 4)
            from groups in Gen.ArrayOf(GenBinaryDigitGroup(), count)
            select string.Concat(groups.Select(g => "_" + g));
    }

    /// <summary>
    /// Property 1: Valid binary literals tokenize as BINARY_LITERAL.
    ///
    /// For any valid binary literal string, the lexer produces exactly one
    /// BINARY_LITERAL token whose lexeme equals the input.
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(BinaryLiteralTokenPropertyTests) })]
    public bool ValidBinaryLiteral_TokenizesAsSingleBinaryLiteralToken(string literal)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", literal);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);

        var tokens = stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();

        // Must produce exactly one token
        if(tokens.Count != 1)
        {
            return false;
        }

        // Token must be BINARY_LITERAL
        if(tokens[0].Kind != (int)TokenKind.BINARY_LITERAL)
        {
            return false;
        }

        // Lexeme must match the input exactly
        if(tokens[0].Lexeme != literal)
        {
            return false;
        }

        return true;
    }
}