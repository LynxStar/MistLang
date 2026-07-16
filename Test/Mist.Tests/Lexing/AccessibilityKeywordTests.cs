using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;

namespace Mist.Tests.Lexing;

/// <summary>
/// Unit tests verifying that the lexer correctly recognizes all accessibility
/// keywords (`public`, `private`, `protected`, `internal`) and the `sealed`
/// modifier keyword.
///
/// Requirements: 1.1, 1.2, 1.3, 1.4, 1.5
/// </summary>
public class AccessibilityKeywordTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();

    /// <summary>
    /// Helper: lex a source string and return the default-channel tokens
    /// (excluding hidden tokens like whitespace/comments), also excluding EOF.
    /// </summary>
    private static IReadOnlyList<Storm.Runtime.Token> LexDefaultTokens(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var stream = Lexer.Lex(sourceText, bag);
        return stream.DefaultChannelTokens
            .Where(t => t.Kind != (int)TokenKind.EndOfFile)
            .ToList();
    }

    // ─── Requirement 1.1: `public` recognized as keyword token ───────────────

    [Fact]
    public void Public_LexesAsKeywordToken()
    {
        var tokens = LexDefaultTokens("public");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.PUBLIC, tokens[0].Kind);
        Assert.Equal("public", tokens[0].Lexeme);
    }

    // ─── Requirement 1.2: `private` recognized as keyword token ──────────────

    [Fact]
    public void Private_LexesAsKeywordToken()
    {
        var tokens = LexDefaultTokens("private");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.PRIVATE, tokens[0].Kind);
        Assert.Equal("private", tokens[0].Lexeme);
    }

    // ─── Requirement 1.3: `protected` recognized as keyword token ────────────

    [Fact]
    public void Protected_LexesAsKeywordToken()
    {
        var tokens = LexDefaultTokens("protected");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.PROTECTED, tokens[0].Kind);
        Assert.Equal("protected", tokens[0].Lexeme);
    }

    // ─── Requirement 1.4: `internal` recognized as keyword token ─────────────

    [Fact]
    public void Internal_LexesAsKeywordToken()
    {
        var tokens = LexDefaultTokens("internal");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.INTERNAL, tokens[0].Kind);
        Assert.Equal("internal", tokens[0].Lexeme);
    }

    // ─── Requirement 1.5: `sealed` recognized as keyword token ───────────────

    [Fact]
    public void Sealed_LexesAsKeywordToken()
    {
        var tokens = LexDefaultTokens("sealed");

        _ = Assert.Single(tokens);
        Assert.Equal((int)TokenKind.SEALED, tokens[0].Kind);
        Assert.Equal("sealed", tokens[0].Lexeme);
    }
}