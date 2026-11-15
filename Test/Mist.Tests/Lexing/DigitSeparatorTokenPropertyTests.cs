// binary-literals Property 2, Requirements 8.1, 8.2, 8.3, 8.9.
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Lexing;

public class DigitSeparatorTokenPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();

    private static readonly string[] IntSuffixes =
        ["", "l", "L", "u", "U", "UL", "ul", "Ul", "uL", "Lu", "lU", "LU", "lu"];

    private static readonly string[] FloatSuffixes =
        ["f", "F", "d", "D", "m", "M"];

    private static readonly string[] FloatSuffixesWithEmpty =
        ["", "f", "F", "d", "D", "m", "M"];

    private static Gen<string> GenDecimalDigitGroup()
    {
        return
            from length in Gen.Choose(1, 6)
            from digits in Gen.ArrayOf(Gen.Choose(0, 9), length)
            select string.Concat(digits);
    }

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

    private static Gen<string> GenBinaryDigitGroup()
    {
        return
            from length in Gen.Choose(1, 8)
            from digits in Gen.ArrayOf(Gen.Elements('0', '1'), length)
            select new string(digits);
    }

    /// <summary>
    /// At least one separator group is always generated to ensure the literal contains separators.
    /// </summary>
    private static Gen<string> GenSeparatorGroups(Gen<string> digitGroupGen)
    {
        return
            from count in Gen.Choose(1, 4)
            from groups in Gen.ArrayOf(digitGroupGen, count)
            select string.Concat(groups.Select(g => "_" + g));
    }

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

    public static Gen<string> GenHexWithSeparators()
    {
        return
            from prefix in Gen.Elements("0x", "0X")
            from firstGroup in GenHexDigitGroup()
            from separatorGroups in GenSeparatorGroups(GenHexDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select prefix + firstGroup + separatorGroups + suffix;
    }

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

    public static Gen<string> GenBinaryWithSeparators()
    {
        return
            from prefix in Gen.Elements("0b", "0B")
            from firstGroup in GenBinaryDigitGroup()
            from separatorGroups in GenSeparatorGroups(GenBinaryDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select prefix + firstGroup + separatorGroups + suffix;
    }

    private static IReadOnlyList<Storm.Runtime.Token> LexDefaultTokens(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        return stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();
    }

    private static bool VerifyToken(string literal, TokenKind expectedKind)
    {
        var tokens = LexDefaultTokens(literal);

        if(tokens.Count != 1)
        {
            return false;
        }

        if(tokens[0].Kind != (int)expectedKind)
        {
            return false;
        }

        if(tokens[0].Lexeme != literal)
        {
            return false;
        }

        return true;
    }

    [Property(MaxTest = 100)]
    public Property IntegerWithSeparators_TokenizesAsIntegerLiteral()
    {
        return Prop.ForAll(
            GenIntegerWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.INTEGER_LITERAL));
    }

    [Property(MaxTest = 100)]
    public Property HexWithSeparators_TokenizesAsHexLiteral()
    {
        return Prop.ForAll(
            GenHexWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.HEX_LITERAL));
    }

    [Property(MaxTest = 100)]
    public Property FloatDotWithSeparators_TokenizesAsFloatLiteral()
    {
        return Prop.ForAll(
            GenFloatDotWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.FLOAT_LITERAL));
    }

    [Property(MaxTest = 100)]
    public Property FloatSuffixWithSeparators_TokenizesAsFloatLiteral()
    {
        return Prop.ForAll(
            GenFloatSuffixWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.FLOAT_LITERAL));
    }

    [Property(MaxTest = 100)]
    public Property BinaryWithSeparators_TokenizesAsBinaryLiteral()
    {
        return Prop.ForAll(
            GenBinaryWithSeparators().ToArbitrary(),
            literal => VerifyToken(literal, TokenKind.BINARY_LITERAL));
    }
}