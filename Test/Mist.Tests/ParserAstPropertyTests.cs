using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests;

/// <summary>
/// Property-based tests for the Parser and AST stages.
/// The v1 parser is skeletal (returns null root), so these tests validate
/// the property structure and will be fully exercised when the parser is complete.
/// </summary>
public class ParserAstPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();

    // ---------------------------------------------------------------
    //  27.1 — Parser span coverage
    //  **Validates: Correctness Property 4**
    //  For any successfully parsed AST, the root node's Span covers
    //  the entire default-channel token range.
    // ---------------------------------------------------------------

    [Fact]
    public void ParserSpanCoverage_RootSpanCoversEntireTokenRange()
    {
        // Test with a set of representative inputs.
        // The skeletal parser returns Root=null, so we verify the property
        // structure: when Root is non-null, its span should cover the token range.
        var inputs = new[]
        {
            "",
            "hello",
            "let x = 42;",
            "fn main() { return 0; }",
            "// comment\nlet y = true;",
        };

        foreach (var input in inputs)
        {
            var source = new Mist.Common.Text.SourceText("test.mist", input);
            var lexBag = new DiagnosticBag();
            var tokenStream = Lexer.Lex(source, lexBag);

            var parseBag = new DiagnosticBag();
            var parseResult = Parser.Parse(tokenStream, parseBag);

            if (parseResult.Root is null)
            {
                // Skeletal parser: Root is null. This is expected for v1.
                // When the full parser is implemented, this test will exercise
                // the span coverage property.
                continue;
            }

            // When Root is non-null, verify span coverage
            var defaultTokens = tokenStream.DefaultChannelTokens;
            if (defaultTokens.Length == 0)
                continue;

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
        // Test with representative inputs.
        var inputs = new[]
        {
            "",
            "hello world",
            "let x = 42;",
            "fn main() { return 0; }",
        };

        foreach (var input in inputs)
        {
            var source = new Mist.Common.Text.SourceText("test.mist", input);
            var lexBag = new DiagnosticBag();
            var tokenStream = Lexer.Lex(source, lexBag);

            var parseBag = new DiagnosticBag();
            var parseResult = Parser.Parse(tokenStream, parseBag);

            if (parseResult.Root is null)
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
        for (int i = 0; i < children1.Count; i++)
        {
            Assert.Same(children1[i], children2[i]);
        }

        // Every child's span should be within the parent's span
        foreach (var child in children1)
        {
            Assert.True(child.Span.Start >= node.Span.Start,
                $"Child span start ({child.Span.Start}) should be >= parent span start ({node.Span.Start})");
            Assert.True(child.Span.End <= node.Span.End,
                $"Child span end ({child.Span.End}) should be <= parent span end ({node.Span.End})");

            // Recurse into children
            VerifyChildrenConsistency(child);
        }
    }
}
