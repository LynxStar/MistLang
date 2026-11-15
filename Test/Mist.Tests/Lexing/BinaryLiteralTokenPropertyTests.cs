// binary-literals Property 1, Requirements 1.1, 1.3, 1.5.
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Lexing;

public class BinaryLiteralTokenPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();

    private static readonly string[] Suffixes =
        ["", "l", "L", "u", "U", "UL", "ul", "Ul", "uL", "Lu", "lU", "LU", "lu"];

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

    private static Gen<string> GenBinaryDigitGroup()
    {
        return
            from length in Gen.Choose(1, 8)
            from digits in Gen.ArrayOf(Gen.Elements('0', '1'), length)
            select new string(digits);
    }

    private static Gen<string> GenSeparatorGroups()
    {
        return
            from count in Gen.Choose(0, 4)
            from groups in Gen.ArrayOf(GenBinaryDigitGroup(), count)
            select string.Concat(groups.Select(g => "_" + g));
    }

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(BinaryLiteralTokenPropertyTests) })]
    public bool ValidBinaryLiteral_TokenizesAsSingleBinaryLiteralToken(string literal)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", literal);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);

        var tokens = stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();

        if(tokens.Count != 1)
        {
            return false;
        }

        if(tokens[0].Kind != (int)TokenKind.BINARY_LITERAL)
        {
            return false;
        }

        if(tokens[0].Lexeme != literal)
        {
            return false;
        }

        return true;
    }
}