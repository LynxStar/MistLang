// type-unions Property 1, Requirements 1.2, 1.3.
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Lexing;

public class UnionLexerPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();

    public static Arbitrary<string> ArbitraryPipeSource()
    {
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

    private static IReadOnlyList<Storm.Runtime.Token> LexDefaultTokens(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        return stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();
    }

    /// <summary>
    /// Uses the same maximal munch logic: consume `||` greedily, then single `|`.
    /// </summary>
    private static List<(int Offset, bool IsLogicalOr)> ExpectedPipeTokens(string source)
    {
        var result = new List<(int Offset, bool IsLogicalOr)>();
        var i = 0;
        while(i < source.Length)
        {
            if(source[i] == '|')
            {
                if(i + 1 < source.Length && source[i + 1] == '|')
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

    [Property(MaxTest = 100)]
    public Property MaximalMunch_PipeVsLogicalOr_CorrectTokenKinds()
    {
        return Prop.ForAll(
            ArbitraryPipeSource(),
            source =>
            {
                var tokens = LexDefaultTokens(source);
                var expected = ExpectedPipeTokens(source);

                var pipeTokens = tokens
                    .Where(t => t.Kind is ((int)TokenKind.PIPE) or ((int)TokenKind.LOGICAL_OR))
                    .ToList();

                if(pipeTokens.Count != expected.Count)
                {
                    return false;
                }

                for(var i = 0; i < expected.Count; i++)
                {
                    var expectedKind = expected[i].IsLogicalOr
                        ? (int)TokenKind.LOGICAL_OR
                        : (int)TokenKind.PIPE;

                    if(pipeTokens[i].Kind != expectedKind)
                    {
                        return false;
                    }
                }

                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property MaximalMunch_NoConsecutivePipesFormLogicalOr()
    {
        return Prop.ForAll(
            ArbitraryPipeSource(),
            source =>
            {
                var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
                var bag = new DiagnosticBag();
                var stream = Lexer.Lex(sourceText, bag);

                // Get ALL tokens (including hidden/whitespace) to check adjacency
                var allTokens = stream.AllTokens
                    .Where(t => t.Kind != (int)TokenKind.EndOfFile)
                    .ToList();

                for(var i = 0; i < allTokens.Count - 1; i++)
                {
                    var current = allTokens[i];
                    var next = allTokens[i + 1];

                    if(current.Kind == (int)TokenKind.PIPE &&
                        next.Kind == (int)TokenKind.PIPE &&
                        current.Span.Start + current.Lexeme.Length == next.Span.Start)
                    {
                        return false;
                    }
                }

                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property MaximalMunch_TokenLexemesAreCorrect()
    {
        return Prop.ForAll(
            ArbitraryPipeSource(),
            source =>
            {
                var tokens = LexDefaultTokens(source);

                foreach(var token in tokens)
                {
                    if(token.Kind == (int)TokenKind.PIPE && token.Lexeme != "|")
                    {
                        return false;
                    }

                    if(token.Kind == (int)TokenKind.LOGICAL_OR && token.Lexeme != "||")
                    {
                        return false;
                    }
                }

                return true;
            });
    }
}