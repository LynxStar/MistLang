// Feature: binary-literals, Property 2: Valid numeric literals with digit separators tokenize correctly
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

/// <summary>
/// Property-based tests for digit separator tokenization across all numeric literal types.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.9**
///
/// Property 2: For any valid numeric literal with digit separators (integer, hex, float,
/// or binary), the lexer SHALL produce a single token of the appropriate kind whose lexeme
/// equals the input string.
/// </summary>
public class DigitSeparatorTokenPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();

    // ─── Suffix Constants ────────────────────────────────────────────────────

    private static readonly string[] IntSuffixes =
        ["", "l", "L", "u", "U", "UL", "ul", "Ul", "uL", "Lu", "lU", "LU", "lu"];

    private static readonly string[] FloatSuffixes =
        ["f", "F", "d", "D", "m", "M"];

    private static readonly string[] FloatSuffixesWithEmpty =
        ["", "f", "F", "d", "D", "m", "M"];

    // ─── Shared Generators ───────────────────────────────────────────────────

    /// <summary>
    /// Generates a group of 1–6 decimal digits.
    /// </summary>
    private static Gen<string> GenDecimalDigitGroup()
    {
        return
            from length in Gen.Choose(1, 6)
            from digits in Gen.ArrayOf(Gen.Choose(0, 9), length)
            select string.Concat(digits);
    }

    /// <summary>
    /// Generates a group of 1–6 hex digits.
    /// </summary>
    private static Gen<string> GenHexDigitGroup()
    {
        return
            from length in Gen.Choose(1, 6)
            from digits in Gen.ArrayOf(Gen.Elements(
                '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
                'a', 'b', 'c', 'd', 'e', 'f',
                'A', 'B', 'C', 'D', 'E', 'F'), length)
            select new string(digits);
    }

    /// <summary>
    /// Generates a group of 1–8 binary digits.
    /// </summary>
    private static Gen<string> GenBinaryDigitGroup()
    {
        return
            from length in Gen.Choose(1, 8)
            from digits in Gen.ArrayOf(Gen.Elements('0', '1'), length)
            select new string(digits);
    }

    /// <summary>
    /// Generates 1–4 separator groups ("_" + digit-group) using the given digit group generator.
    /// At least one separator group is always generated to ensure the literal contains separators.
    /// </summary>
    private static Gen<string> GenSeparatorGroups(Gen<string> digitGroupGen)
    {
        return
            from count in Gen.Choose(1, 4)
            from groups in Gen.ArrayOf(digitGroupGen, count)
            select string.Concat(groups.Select(g => "_" + g));
    }

    // ─── Integer Literal with Separators ─────────────────────────────────────

    /// <summary>
    /// Generates a valid integer literal with at least one digit separator.
    /// Grammar: [0-9]+ ("_" [0-9]+)* INT_SUFFIX?
    ///
    /// To avoid accidentally matching hex (0x) or binary (0b) patterns,
    /// the first digit group starts with 1-9 when multi-digit, or is a single digit 0-9.
    /// </summary>
    public static Gen<string> GenIntegerWithSeparators()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 5)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from separatorGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select firstDigit + string.Concat(restDigits) + separatorGroups + suffix;
    }

    // ─── Hex Literal with Separators ─────────────────────────────────────────

    /// <summary>
    /// Generates a valid hex literal with at least one digit separator.
    /// Grammar: "0" [xX] HEX_DIGIT+ ("_" HEX_DIGIT+)* INT_SUFFIX?
    /// </summary>
    public static Gen<string> GenHexWithSeparators()
    {
        return
            from prefix in Gen.Elements("0x", "0X")
            from firstGroup in GenHexDigitGroup()
            from separatorGroups in GenSeparatorGroups(GenHexDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select prefix + firstGroup + separatorGroups + suffix;
    }

    // ─── Float Literal with Separators (dot form) ────────────────────────────

    /// <summary>
    /// Generates a valid float literal (dot form) with at least one digit separator.
    /// Grammar: [0-9]+ ("_" [0-9]+)* "." [0-9]+ ("_" [0-9]+)* FLOAT_SUFFIX?
    ///
    /// The integer part starts with 1-9 to avoid 0x/0b prefix conflicts.
    /// </summary>
    public static Gen<string> GenFloatDotWithSeparators()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 5)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from intSepGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from fracFirstGroup in GenDecimalDigitGroup()
            from fracSepGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(FloatSuffixesWithEmpty)
            select firstDigit + string.Concat(restDigits) + intSepGroups
                   + "." + fracFirstGroup + fracSepGroups + suffix;
    }

    /// <summary>
    /// Generates a valid float literal (suffix-only form) with at least one digit separator.
    /// Grammar: [0-9]+ ("_" [0-9]+)* FLOAT_SUFFIX
    ///
    /// The integer part starts with 1-9 to avoid 0x/0b prefix conflicts.
    /// </summary>
    public static Gen<string> GenFloatSuffixWithSeparators()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 5)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from separatorGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(FloatSuffixes)
            select firstDigit + string.Concat(restDigits) + separatorGroups + suffix;
    }

    // ─── Binary Literal with Separators ──────────────────────────────────────

    /// <summary>
    /// Generates a valid binary literal with at least one digit separator.
    /// Grammar: "0" [bB] BINARY_DIGIT+ ("_" BINARY_DIGIT+)* INT_SUFFIX?
    /// </summary>
    public static Gen<string> GenBinaryWithSeparators()
    {
        return
            from prefix in Gen.Elements("0b", "0B")
            from firstGroup in GenBinaryDigitGroup()
            from separatorGroups in GenSeparatorGroups(GenBinaryDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select prefix + firstGroup + separatorGroups + suffix;
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lex a source string and return the default-channel tokens (excluding EOF).
    /// </summary>
    private static IReadOnlyList<Storm.Runtime.Token> LexDefaultTokens(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        return stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();
    }

    /// <summary>
    /// Verifies that a literal lexes as a single token of the expected kind with matching lexeme.
    /// </summary>
    private static bool VerifyToken(string literal, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(literal);

        if(tokens.Count != 1)
            return false;

        if(tokens[0].Kind != (int)expectedKind)
            return false;

        if(tokens[0].Lexeme != literal)
            return false;

        return true;
    }

    // ─── Property Tests ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 2a: Valid integer literals with digit separators tokenize as INTEGER_LITERAL.
    /// **Validates: Requirements 8.1, 8.9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IntegerWithSeparators_TokenizesAsIntegerLiteral()
    {
        return Prop.ForAll(
            GenIntegerWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.INTEGER_LITERAL));
    }

    /// <summary>
    /// Property 2b: Valid hex literals with digit separators tokenize as HEX_LITERAL.
    /// **Validates: Requirements 8.2, 8.9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HexWithSeparators_TokenizesAsHexLiteral()
    {
        return Prop.ForAll(
            GenHexWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.HEX_LITERAL));
    }

    /// <summary>
    /// Property 2c: Valid float literals (dot form) with digit separators tokenize as FLOAT_LITERAL.
    /// **Validates: Requirements 8.3, 8.9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FloatDotWithSeparators_TokenizesAsFloatLiteral()
    {
        return Prop.ForAll(
            GenFloatDotWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.FLOAT_LITERAL));
    }

    /// <summary>
    /// Property 2d: Valid float literals (suffix-only form) with digit separators tokenize as FLOAT_LITERAL.
    /// **Validates: Requirements 8.3, 8.9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FloatSuffixWithSeparators_TokenizesAsFloatLiteral()
    {
        return Prop.ForAll(
            GenFloatSuffixWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.FLOAT_LITERAL));
    }

    /// <summary>
    /// Property 2e: Valid binary literals with digit separators tokenize as BINARY_LITERAL.
    /// **Validates: Requirements 8.1, 8.9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BinaryWithSeparators_TokenizesAsBinaryLiteral()
    {
        return Prop.ForAll(
            GenBinaryWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.BINARY_LITERAL));
    }
}