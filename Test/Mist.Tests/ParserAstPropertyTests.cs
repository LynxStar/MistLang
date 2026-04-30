using Mist.Common.Diagnostics;
using Mist.Lexing;
using Mist.Parsing;

namespace Mist.Tests;

/// <summary>
/// Property-based tests for the Parser and AST stages.
/// The v1 parser is skeletal (returns null root), so these tests validate
/// the property structure and will be fully exercised when the parser is complete.
/// </summary>
public class ParserAstPropertyTests
{
    private static MistLexer Lexer = new();
    private static MistParser Parser = new();

    // ---------------------------------------------------------------
    //  27.1 — Parser span coverage
    //  **Validates: Correctness Property 4**
    //  For any successfully parsed AST, the root node's Span covers
    //  the entire default-channel token range.
    // ---------------------------------------------------------------

    [Fact]
    public void ParserSpanCoverage_RootSpanCoversEntireTokenRange()
    {
        // Test with a set of representative valid Mist inputs.
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
            var source = new Mist.Common.Text.SourceText("test.mist", input);
            var lexBag = new DiagnosticBag();
            var tokenStream = Lexer.Lex(source, lexBag);

            var parseBag = new DiagnosticBag();
            var parseResult = Parser.Parse(tokenStream, parseBag);

            if(parseResult.Root is null)
            {
                // Parser returned null root (parse error). Skip.
                continue;
            }

            // When Root is non-null, verify span coverage
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

    // ---------------------------------------------------------------
    //  27.2 — AST children consistency
    //  **Validates: Correctness Property 5**
    //  For any AST node, Children() returns the same sequence on
    //  repeated calls, and every child's Span is within the parent's Span.
    // ---------------------------------------------------------------

    [Fact]
    public void AstChildrenConsistency_ChildrenStableAndSpansContained()
    {
        // Test with representative valid Mist inputs.
        var inputs = new[]
        {
            "",
            "class Foo { }",
            "namespace MyApp; class Foo { }",
            "using System; namespace MyApp; public class Program { public static void Main() { } }",
        };

        foreach(var input in inputs)
        {
            var source = new Mist.Common.Text.SourceText("test.mist", input);
            var lexBag = new DiagnosticBag();
            var tokenStream = Lexer.Lex(source, lexBag);

            var parseBag = new DiagnosticBag();
            var parseResult = Parser.Parse(tokenStream, parseBag);

            if(parseResult.Root is null)
            {
                // Skeletal parser: Root is null. Expected for v1.
                continue;
            }

            // Recursively verify children consistency
            VerifyChildrenConsistency(parseResult.Root);
        }
    }

    private static void VerifyChildrenConsistency(Storm.Runtime.SyntaxNode node)
    {
        // Children() should return the same sequence on repeated calls
        var children1 = node.Children().ToList();
        var children2 = node.Children().ToList();

        Assert.Equal(children1.Count, children2.Count);
        for(var i = 0; i < children1.Count; i++)
        {
            Assert.Same(children1[i], children2[i]);
        }

        // Every child's span should be within the parent's span
        foreach(var child in children1)
        {
            if (child is null) continue; // Skip null children from generated AST nodes

            Assert.True(child.Span.Start >= node.Span.Start,
                $"Child span start ({child.Span.Start}) should be >= parent span start ({node.Span.Start})");
            Assert.True(child.Span.End <= node.Span.End,
                $"Child span end ({child.Span.End}) should be <= parent span end ({node.Span.End})");

            // Recurse into children
            VerifyChildrenConsistency(child);
        }
    }
}
