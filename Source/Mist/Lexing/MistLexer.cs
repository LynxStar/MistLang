using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Lexing.Contracts;

namespace Mist.Lexing;

using Token = Storm.Runtime.Token;
using SourceSpan = Storm.Runtime.SourceSpan;
using LinePosition = Storm.Runtime.LinePosition;
using TokenChannel = Storm.Runtime.TokenChannel;

/// <summary>
/// Wraps the Storm-generated lexer behind the <see cref="ILexer"/> interface.
///
/// The Storm source generator cannot handle context-sensitive tokens like
/// interpolated strings. This wrapper post-processes the Storm-generated token
/// stream to fix up interpolated string regions.
///
/// The Storm lexer sees $"Hello {name}" and produces:
///   Error($) + STRING_LITERAL("Hello {name}")
/// This wrapper detects that pattern and re-lexes the region as proper
/// interpolated string tokens.
/// </summary>
public sealed class MistLexer : ILexer
{
    public Contracts.TokenStream Lex(Common.Text.SourceText source, DiagnosticBag diagnostics)
    {
        var stormSource = new Storm.Runtime.SourceText(source.Path, source.Contents);
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();

        var stormTokenStream = Mist.Generated.MistLexer.Lex(stormSource, stormDiagnostics);

        // Post-process: fix up interpolated strings
        var fixedTokens = FixInterpolatedStrings(
            source.Contents, source.Path, stormTokenStream.AllTokens, diagnostics);

        // Map remaining Storm diagnostics (skip '$' errors we've handled)
        foreach (var msg in stormDiagnostics.Messages)
        {
            if (msg.Contains("Unexpected character '$'"))
                continue;

            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_LEX_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Lexing,
                msg,
                Location: null,
                CompilationUnitPath: source.Path,
                AnalyzerName: null));
        }

        return new Contracts.TokenStream(source, fixedTokens);
    }

    private static ImmutableArray<Token> FixInterpolatedStrings(
        string input, string filePath,
        ImmutableArray<Token> tokens, DiagnosticBag diagnostics)
    {
        var result = ImmutableArray.CreateBuilder<Token>();

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            // Detect Error('$') immediately followed by STRING_LITERAL
            if (token.Kind == (int)Mist.Generated.TokenKind.Error
                && token.Lexeme == "$"
                && i + 1 < tokens.Length
                && tokens[i + 1].Kind == (int)Mist.Generated.TokenKind.STRING_LITERAL
                && tokens[i + 1].Span.Start == token.Span.Start + 1)
            {
                var interpolatedTokens = LexInterpolatedString(
                    input, token.Span.Start, token.Start, filePath, diagnostics);
                result.AddRange(interpolatedTokens);
                i++; // Skip the STRING_LITERAL token
                continue;
            }

            result.Add(token);
        }

        return result.ToImmutable();
    }

    private static List<Token> LexInterpolatedString(
        string input, int startOffset, LinePosition startPos,
        string filePath, DiagnosticBag diagnostics)
    {
        var tokens = new List<Token>();
        var pos = startOffset;
        var line = startPos.Line;
        var col = startPos.Column;

        // Emit INTERPOLATED_STRING_START for '$"'
        tokens.Add(new Token(
            (int)Mist.Generated.TokenKind.INTERPOLATED_STRING_START,
            new SourceSpan(pos, 2),
            new LinePosition(line, col),
            new LinePosition(line, col + 2),
            TokenChannel.Default,
            "$\""));
        pos += 2;
        col += 2;

        while (pos < input.Length)
        {
            var ch = input[pos];

            if (ch == '"')
            {
                // End of interpolated string
                tokens.Add(new Token(
                    (int)Mist.Generated.TokenKind.INTERPOLATED_STRING_END,
                    new SourceSpan(pos, 1),
                    new LinePosition(line, col),
                    new LinePosition(line, col + 1),
                    TokenChannel.Default,
                    "\""));
                break;
            }
            else if (ch == '{')
            {
                // Expression hole
                tokens.Add(new Token(
                    (int)Mist.Generated.TokenKind.LEFT_BRACE,
                    new SourceSpan(pos, 1),
                    new LinePosition(line, col),
                    new LinePosition(line, col + 1),
                    TokenChannel.Default,
                    "{"));
                pos++;
                col++;

                // Find matching closing brace
                int braceDepth = 1;
                int exprStart = pos;
                while (pos < input.Length && braceDepth > 0)
                {
                    if (input[pos] == '{') braceDepth++;
                    else if (input[pos] == '}') braceDepth--;
                    if (braceDepth > 0) pos++;
                }

                int exprEnd = pos;
                if (exprStart < exprEnd)
                {
                    var exprText = input.Substring(exprStart, exprEnd - exprStart);
                    var exprTokens = LexExpression(
                        exprText, exprStart, line, col, filePath, diagnostics);
                    tokens.AddRange(exprTokens);

                    foreach (var c in exprText)
                    {
                        if (c == '\n') { line++; col = 1; } else { col++; }
                    }
                }

                // Emit RIGHT_BRACE
                if (pos < input.Length && input[pos] == '}')
                {
                    tokens.Add(new Token(
                        (int)Mist.Generated.TokenKind.RIGHT_BRACE,
                        new SourceSpan(pos, 1),
                        new LinePosition(line, col),
                        new LinePosition(line, col + 1),
                        TokenChannel.Default,
                        "}"));
                    pos++;
                    col++;
                }
            }
            else
            {
                // Text segment
                var textStart = pos;
                var textLineStart = line;
                var textColStart = col;

                while (pos < input.Length)
                {
                    var c = input[pos];
                    if (c == '"' || c == '{') break;

                    if (c == '\\' && pos + 1 < input.Length)
                    {
                        col++; pos++;
                        if (input[pos] == '\n') { line++; col = 1; } else { col++; }
                        pos++;
                    }
                    else
                    {
                        if (c == '\n') { line++; col = 1; } else { col++; }
                        pos++;
                    }
                }

                var textLen = pos - textStart;
                if (textLen > 0)
                {
                    tokens.Add(new Token(
                        (int)Mist.Generated.TokenKind.INTERPOLATED_STRING_TEXT,
                        new SourceSpan(textStart, textLen),
                        new LinePosition(textLineStart, textColStart),
                        new LinePosition(line, col),
                        TokenChannel.Default,
                        input.Substring(textStart, textLen)));
                }
            }
        }

        return tokens;
    }

    private static List<Token> LexExpression(
        string exprText, int sourceOffset, int startLine, int startCol,
        string filePath, DiagnosticBag diagnostics)
    {
        var tokens = new List<Token>();
        var stormSource = new Storm.Runtime.SourceText(filePath, exprText);
        var stormDiag = new Storm.Runtime.DiagnosticBag();
        var stormStream = Mist.Generated.MistLexer.Lex(stormSource, stormDiag);

        foreach (var token in stormStream.AllTokens)
        {
            if (token.Kind == (int)Mist.Generated.TokenKind.EndOfFile)
                continue;

            var adjustedSpan = new SourceSpan(
                sourceOffset + token.Span.Start, token.Span.Length);
            var adjustedStart = AdjustLinePosition(token.Start, startLine, startCol);
            var adjustedEnd = AdjustLinePosition(token.End, startLine, startCol);
            var channel = Mist.Generated.MistLexer.IsHidden(token.Kind)
                ? TokenChannel.Hidden : TokenChannel.Default;

            tokens.Add(new Token(
                token.Kind, adjustedSpan, adjustedStart, adjustedEnd,
                channel, token.Lexeme));
        }

        foreach (var msg in stormDiag.Messages)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_LEX_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Lexing,
                msg,
                Location: null,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
        }

        return tokens;
    }

    private static LinePosition AdjustLinePosition(
        LinePosition stormPos, int baseLineOffset, int baseColOffset)
    {
        if (stormPos.Line == 1)
            return new LinePosition(baseLineOffset, baseColOffset + stormPos.Column - 1);
        return new LinePosition(baseLineOffset + stormPos.Line - 1, stormPos.Column);
    }
}
