// Feature: type-unions, Property 1: Lexer Maximal Munch — PIPE vs LOGICAL_OR
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

/// <summary>
/// Property-based tests for the lexer maximal munch behavior with PIPE and LOGICAL_OR tokens.
///
/// **Validates: Requirements 1.2, 1.3**
///
/// Property 1: For any source string containing `|` characters, the lexer SHALL produce
/// PIPE tokens for single `|` and LOGICAL_OR tokens for `||`, never producing two
/// consecutive PIPE tokens where a LOGICAL_OR should appear.
/// </summary>
public class UnionLexerPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();

    // ─── Generators ──────────────────────────────────────────────────────────

    /// <summary>
    /// Generates random source strings that contain a mix of `|`, `||`, identifiers,
    /// and whitespace. This exercises the lexer's maximal munch behavior.
    /// </summary>
    public static Arbitrary<string> ArbitraryPipeSource()
    {
        // Segments that can appear in the generated source
        var segmentGen = Gen.OneOf(
            Gen.Constant("|"),
            Gen.Constant("||"),
            Gen.Constant(" "),
            Gen.Constant("  "),
            Gen.Elements("x", "y", "int", "string", "bool"),
            Gen.Constant("| |"),
            Gen.Constant("|||")
        );

        var gen =
            from count in Gen.Choose(1, 8)
            from segments in Gen.ArrayOf(segmentGen, count)
            select string.Concat(segments);

        return gen.ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

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
    /// Scans the source string to determine the expected token sequence for pipe characters.
    /// Uses the same maximal munch logic: consume `||` greedily, then single `|`.
    /// Returns a list of (offset, isLogicalOr) pairs for each pipe-related token.
    /// </summary>
    private static List<(int Offset, bool IsLogicalOr)> ExpectedPipeTokens(string source)
    {
        var result = new List<(int Offset, bool IsLogicalOr)>();
        var i = 0;
        while (i < source.Length)
        {
            if (source[i] == '|')
            {
                if (i + 1 < source.Length && source[i + 1] == '|')
                {
                    result.Add((i, true));
                    i += 2;
                }
                else
                {
                    result.Add((i, false));
                    i += 1;
                }
            }
            else
            {
                i++;
            }
        }

        return result;
    }

    // ─── Property Tests ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 1a: Every `||` in the source produces a LOGICAL_OR token, and every
    /// standalone `|` produces a PIPE token.
    ///
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MaximalMunch_PipeVsLogicalOr_CorrectTokenKinds()
    {
        return Prop.ForAll(
            ArbitraryPipeSource(),
            source =>
            {
                var tokens = LexDefaultTokens(source);
                var expected = ExpectedPipeTokens(source);

                // Extract only PIPE and LOGICAL_OR tokens from the lexer output
                var pipeTokens = tokens
                    .Where(t => t.Kind == (int)TokenKind.PIPE || t.Kind == (int)TokenKind.LOGICAL_OR)
                    .ToList();

                // Count must match
                if (pipeTokens.Count != expected.Count)
                    return false;

                // Each token must have the correct kind
                for (var i = 0; i < expected.Count; i++)
                {
                    var expectedKind = expected[i].IsLogicalOr
                        ? (int)TokenKind.LOGICAL_OR
                        : (int)TokenKind.PIPE;

                    if (pipeTokens[i].Kind != expectedKind)
                        return false;
                }

                return true;
            });
    }

    /// <summary>
    /// Property 1b: No two consecutive PIPE tokens appear in the output where a
    /// LOGICAL_OR should have been produced. Specifically, if two PIPE tokens are
    /// adjacent in the token stream (no tokens between them), their combined lexemes
    /// must not form "||" (i.e., they must have whitespace or other characters between them).
    ///
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MaximalMunch_NoConsecutivePipesFormLogicalOr()
    {
        return Prop.ForAll(
            ArbitraryPipeSource(),
            source =>
            {
                var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
                var bag = new DiagnosticBag();
                var stream = Lexer.Lex(sourceText, bag);

                // Get ALL tokens (including hidden/whitespace) to check adjacency
                var allTokens = stream.AllTokens
                    .Where(t => t.Kind != (int)TokenKind.EndOfFile)
                    .ToList();

                for (var i = 0; i < allTokens.Count - 1; i++)
                {
                    var current = allTokens[i];
                    var next = allTokens[i + 1];

                    // If two consecutive tokens are both PIPE and they are adjacent
                    // (no gap between them), that means the lexer failed to produce LOGICAL_OR
                    if (current.Kind == (int)TokenKind.PIPE &&
                        next.Kind == (int)TokenKind.PIPE &&
                        current.Span.Start + current.Lexeme.Length == next.Span.Start)
                    {
                        return false;
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// Property 1c: The lexeme of every PIPE token is exactly "|" and the lexeme of
    /// every LOGICAL_OR token is exactly "||".
    ///
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MaximalMunch_TokenLexemesAreCorrect()
    {
        return Prop.ForAll(
            ArbitraryPipeSource(),
            source =>
            {
                var tokens = LexDefaultTokens(source);

                foreach (var token in tokens)
                {
                    if (token.Kind == (int)TokenKind.PIPE && token.Lexeme != "|")
                        return false;

                    if (token.Kind == (int)TokenKind.LOGICAL_OR && token.Lexeme != "||")
                        return false;
                }

                return true;
            });
    }
}
