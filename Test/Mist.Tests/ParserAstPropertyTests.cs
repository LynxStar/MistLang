using Mist.Common.Diagnostics;
using Mist.Lexing;
using Mist.Parsing;

namespace Mist.Tests;

public class ParserAstPropertyTests
{
    private static MistLexer lexer = new();
    private static MistParser parser = new();

    [Fact]
    public void ParserSpanCoverage_RootSpanCoversEntireTokenRange()
    {
        var inputs = new[]
        {
            "",
            "class Foo { }",
            "namespace MyApp; class Foo { }",
            "using System; namespace MyApp; public class Program { public static void Main() { } }",
            "// comment\nclass Bar { int x; }",
        };

        foreach(var input in inputs)
        {
            var source = new Mist.Common.Source.SourceText("test.mist", input);
            var lexBag = new DiagnosticBag();
            var tokenStream = lexer.Lex(source, lexBag);

            var parseBag = new DiagnosticBag();
            var parseResult = parser.Parse(tokenStream, parseBag);

            if(parseResult.Root is null)
            {
                continue;
            }

            var defaultTokens = tokenStream.DefaultChannelTokens;
            if(defaultTokens.Length == 0)
            {
                continue;
            }

            var firstTokenStart = defaultTokens[0].Span.Start;
            var lastTokenEnd = defaultTokens[^1].Span.End;

            Assert.True(parseResult.Root.Span.Start <= firstTokenStart,
                $"Root span start ({parseResult.Root.Span.Start}) should be <= first token start ({firstTokenStart})");
            Assert.True(parseResult.Root.Span.End >= lastTokenEnd,
                $"Root span end ({parseResult.Root.Span.End}) should be >= last token end ({lastTokenEnd})");
        }
    }

    [Fact]
    public void AstChildrenConsistency_ChildrenStableAndSpansContained()
    {
        var inputs = new[]
        {
            "",
            "class Foo { }",
            "namespace MyApp; class Foo { }",
            "using System; namespace MyApp; public class Program { public static void Main() { } }",
        };

        foreach(var input in inputs)
        {
            var source = new Mist.Common.Source.SourceText("test.mist", input);
            var lexBag = new DiagnosticBag();
            var tokenStream = lexer.Lex(source, lexBag);

            var parseBag = new DiagnosticBag();
            var parseResult = parser.Parse(tokenStream, parseBag);

            if(parseResult.Root is null)
            {
                continue;
            }

            VerifyChildrenConsistency(parseResult.Root);
        }
    }

    private static void VerifyChildrenConsistency(Storm.Runtime.SyntaxNode node)
    {
        var children1 = node.Children().ToList();
        var children2 = node.Children().ToList();

        Assert.Equal(children1.Count, children2.Count);
        for(var i = 0; i < children1.Count; i++)
        {
            Assert.Same(children1[i], children2[i]);
        }

        foreach(var child in children1)
        {
            if(child is null)
            {
                continue;
            }

            Assert.True(child.Span.Start >= node.Span.Start,
                $"Child span start ({child.Span.Start}) should be >= parent span start ({node.Span.Start})");
            Assert.True(child.Span.End <= node.Span.End,
                $"Child span end ({child.Span.End}) should be <= parent span end ({node.Span.End})");

            VerifyChildrenConsistency(child);
        }
    }
}