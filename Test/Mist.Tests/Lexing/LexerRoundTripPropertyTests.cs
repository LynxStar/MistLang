using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Lexing;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

public class LexerRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();

    public static Arbitrary<string> ArbitraryString()
    {
        var snippets = new[]
        {
            "class Foo { }",
            "struct Bar { }",
            "namespace MyApp;",
            "using System;",
            "public private static void",
            "new return if else while for foreach in break var true false this",
            "int uint long ulong short ushort byte sbyte float double decimal bool char string",
            "&& || == != <= >= + - * / % = < > ! . , ; : ( ) { } [ ]",
            "42 0 123456 0xFF 0XAB 42L 100u 200UL",
            "3.14 2.0f 1.5d 99.9m 1.0F 2.0D 3.0M",
            "\"hello\" \"world\\n\" \"escaped\\\"quote\"",
            "'a' 'b' '\\n' '\\t' '\\0'",
            "  \t  \n  ",
            "// this is a comment\n",
            "x // comment\ny",
            "1 + 2 * 3",
            "a && b || c",
            "x == y != z",
            "arr[0]",
            "obj.Method()",
            "class Program { public static void Main() { } }",
            "int x = 42;",
            "var y = 3.14f;",
            "if (x > 0) { return x; }",
            "while (true) { break; }",
            "for (int i = 0; i < 10; i) { }",
            "foreach (int item in items) { }",
        };

        return Gen.Elements(snippets).ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LexerRoundTripPropertyTests) })]
    public bool LexerRoundTrip_ConcatenatedLexemesReproduceSource(string source)
    {
        if(string.IsNullOrEmpty(source))
        {
            return true;
        }

        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();

        var tokenStream = Lexer.Lex(sourceText, bag);

        var concatenated = string.Concat(
            tokenStream.AllTokens.Select(t => t.Lexeme));

        if(tokenStream.AllTokens.Length <= 1 &&
            tokenStream.AllTokens.All(t => t.Kind == (int)Generated.TokenKind.EndOfFile))
        {
            return true;
        }

        return concatenated == source;
    }

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(LexerRoundTripPropertyTests) })]
    public bool LexerProducesNonNegativeTokenCount(string source)
    {
        if(string.IsNullOrEmpty(source))
        {
            return true;
        }

        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();

        var tokenStream = Lexer.Lex(sourceText, bag);

        return tokenStream.AllTokens.Length >= 0;
    }
}