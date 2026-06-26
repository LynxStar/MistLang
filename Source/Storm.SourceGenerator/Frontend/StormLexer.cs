using System;
using System.Collections.Generic;
using Storm.SourceGenerator.Diagnostics;

namespace Storm.SourceGenerator.Frontend
{
    /// <summary>
    /// Discriminates the kind of a <see cref="StormToken"/>.
    /// </summary>
    public enum StormTokenKind
    {
        // --- Keywords ---
        /// <summary>The <c>rule</c> keyword.</summary>
        RuleKeyword,
        /// <summary>The <c>token</c> keyword.</summary>
        TokenKeyword,
        /// <summary>The <c>fragment</c> keyword.</summary>
        FragmentKeyword,
        /// <summary>The <c>hidden</c> keyword.</summary>
        HiddenKeyword,
        /// <summary>The <c>import</c> keyword.</summary>
        ImportKeyword,
        /// <summary>The <c>grammar</c> keyword.</summary>
        GrammarKeyword,
        /// <summary>The <c>as</c> keyword.</summary>
        AsKeyword,
        /// <summary>The <c>mode</c> keyword.</summary>
        ModeKeyword,
        /// <summary>The <c>default</c> keyword.</summary>
        DefaultKeyword,
        /// <summary>The <c>push</c> keyword.</summary>
        PushKeyword,
        /// <summary>The <c>pop</c> keyword.</summary>
        PopKeyword,

        // --- Literals and identifiers ---
        /// <summary>An identifier: [a-zA-Z_][a-zA-Z0-9_]*</summary>
        Identifier,
        /// <summary>A double-quoted string literal.</summary>
        StringLiteral,
        /// <summary>A character class: <c>[...]</c></summary>
        CharClass,
        /// <summary>A backtick-delimited regex literal.</summary>
        RegexLiteral,

        // --- Operators / punctuation ---
        /// <summary><c>=</c></summary>
        Equals,
        /// <summary><c>|</c></summary>
        Pipe,
        /// <summary><c>;</c></summary>
        Semicolon,
        /// <summary><c>(</c></summary>
        LeftParen,
        /// <summary><c>)</c></summary>
        RightParen,
        /// <summary><c>?</c></summary>
        Question,
        /// <summary><c>*</c></summary>
        Star,
        /// <summary><c>+</c></summary>
        Plus,
        /// <summary><c>:</c></summary>
        Colon,
        /// <summary><c>.</c></summary>
        Dot,
        /// <summary><c>=&gt;</c></summary>
        FatArrow,
        /// <summary><c>{</c></summary>
        LeftBrace,
        /// <summary><c>}</c></summary>
        RightBrace,

        // --- Annotations ---
        /// <summary>An annotation: <c>@</c> followed by an identifier (e.g., <c>@memo</c>).</summary>
        Annotation,

        // --- Special ---
        /// <summary>End of input.</summary>
        EndOfFile,
        /// <summary>An unrecognized character (error recovery).</summary>
        Error,
    }

    /// <summary>
    /// A single token produced by <see cref="StormLexer"/>.
    /// </summary>
    public readonly struct StormToken
    {
        public StormToken(StormTokenKind kind, string lexeme, int offset, int line, int column)
        {
            Kind = kind;
            Lexeme = lexeme;
            Offset = offset;
            Line = line;
            Column = column;
        }

        /// <summary>The kind of this token.</summary>
        public StormTokenKind Kind { get; }

        /// <summary>The source text of this token.</summary>
        public string Lexeme { get; }

        /// <summary>Zero-based offset into the source text.</summary>
        public int Offset { get; }

        /// <summary>1-based line number.</summary>
        public int Line { get; }

        /// <summary>1-based column number.</summary>
        public int Column { get; }

        public override string ToString()
        {
            return $"{Kind} '{Lexeme}' @{Line}:{Column}";
        }
    }

    /// <summary>
    /// Hand-written lexer for the Storm grammar language.
    /// Produces a list of <see cref="StormToken"/> values and reports
    /// errors via <see cref="StormDiagnostic"/>.
    /// </summary>
    public static class StormLexer
    {
        /// <summary>
        /// Tokenizes the given <paramref name="source"/> text.
        /// </summary>
        /// <param name="source">The Storm source text to tokenize.</param>
        /// <param name="diagnostics">Receives any lexer diagnostics (e.g. unrecognized characters).</param>
        /// <returns>A list of tokens, always ending with an <see cref="StormTokenKind.EndOfFile"/> token.</returns>
        public static List<StormToken> Tokenize(StormSourceText source, List<StormDiagnostic> diagnostics)
        {
            if(source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if(diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var tokens = new List<StormToken>();
            var text = source.Contents;
            var pos = 0;

            while(pos < text.Length)
            {
                var c = text[pos];

                // --- Skip whitespace ---
                if(char.IsWhiteSpace(c))
                {
                    pos++;
                    continue;
                }

                // --- Skip line comments ---
                if(c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                {
                    pos += 2;
                    while(pos < text.Length && text[pos] != '\n' && text[pos] != '\r')
                    {
                        pos++;
                    }

                    continue;
                }

                // Record position for the token about to be produced.
                var (line, column) = source.GetLineAndColumn(pos);

                // --- String literal ---
                if(c == '"')
                {
                    var start = pos;
                    pos++; // skip opening quote
                    while(pos < text.Length && text[pos] != '"')
                    {
                        if(text[pos] == '\\' && pos + 1 < text.Length)
                        {
                            pos += 2; // skip escaped character
                        }
                        else
                        {
                            pos++;
                        }
                    }

                    if(pos < text.Length)
                    {
                        pos++; // skip closing quote
                    }
                    else
                    {
                        diagnostics.Add(new StormDiagnostic(
                            StormDiagnosticIds.STORM_SYNTAX_ERROR,
                            StormDiagnosticSeverity.Error,
                            "Unterminated string literal",
                            source.FilePath,
                            line,
                            column));
                    }

                    var lexeme = text.Substring(start, pos - start);
                    tokens.Add(new StormToken(StormTokenKind.StringLiteral, lexeme, start, line, column));
                    continue;
                }

                // --- Character class ---
                if(c == '[')
                {
                    var start = pos;
                    pos++; // skip '['

                    // Handle negation
                    if(pos < text.Length && text[pos] == '^')
                    {
                        pos++;
                    }

                    // Handle ']' as first character in class (literal ']')
                    if(pos < text.Length && text[pos] == ']')
                    {
                        pos++;
                    }

                    while(pos < text.Length && text[pos] != ']')
                    {
                        if(text[pos] == '\\' && pos + 1 < text.Length)
                        {
                            pos += 2; // skip escaped character
                        }
                        else
                        {
                            pos++;
                        }
                    }

                    if(pos < text.Length)
                    {
                        pos++; // skip closing ']'
                    }
                    else
                    {
                        diagnostics.Add(new StormDiagnostic(
                            StormDiagnosticIds.STORM_SYNTAX_ERROR,
                            StormDiagnosticSeverity.Error,
                            "Unterminated character class",
                            source.FilePath,
                            line,
                            column));
                    }

                    var lexeme = text.Substring(start, pos - start);
                    tokens.Add(new StormToken(StormTokenKind.CharClass, lexeme, start, line, column));
                    continue;
                }

                // --- Regex literal ---
                if(c == '`')
                {
                    var start = pos;
                    pos++; // skip opening backtick
                    while(pos < text.Length && text[pos] != '`')
                    {
                        if(text[pos] == '\\' && pos + 1 < text.Length)
                        {
                            pos += 2; // skip escaped character (including \`)
                        }
                        else
                        {
                            pos++;
                        }
                    }

                    if(pos < text.Length)
                    {
                        pos++; // skip closing backtick
                    }
                    else
                    {
                        diagnostics.Add(new StormDiagnostic(
                            StormDiagnosticIds.STORM_SYNTAX_ERROR,
                            StormDiagnosticSeverity.Error,
                            "Unterminated regex literal",
                            source.FilePath,
                            line,
                            column));
                    }

                    var regexLexeme = text.Substring(start, pos - start);
                    tokens.Add(new StormToken(StormTokenKind.RegexLiteral, regexLexeme, start, line, column));
                    continue;
                }

                // --- Identifiers and keywords ---
                if(IsIdentStart(c))
                {
                    var start = pos;
                    pos++;
                    while(pos < text.Length && IsIdentPart(text[pos]))
                    {
                        pos++;
                    }

                    var lexeme = text.Substring(start, pos - start);
                    var kind = ClassifyKeyword(lexeme);
                    tokens.Add(new StormToken(kind, lexeme, start, line, column));
                    continue;
                }

                // --- Annotation ---
                if(c == '@')
                {
                    var start = pos;
                    pos++; // skip '@'
                    if(pos < text.Length && IsIdentStart(text[pos]))
                    {
                        pos++;
                        while(pos < text.Length && IsIdentPart(text[pos]))
                        {
                            pos++;
                        }

                        var lexeme = text.Substring(start, pos - start);
                        tokens.Add(new StormToken(StormTokenKind.Annotation, lexeme, start, line, column));
                    }
                    else
                    {
                        // '@' not followed by identifier-start: emit error
                        diagnostics.Add(new StormDiagnostic(
                            StormDiagnosticIds.STORM_SYNTAX_ERROR,
                            StormDiagnosticSeverity.Error,
                            "Unrecognized character '@'",
                            source.FilePath,
                            line,
                            column));
                        tokens.Add(new StormToken(StormTokenKind.Error, "@", start, line, column));
                    }

                    continue;
                }

                // --- Two-character operators ---
                if(c == '=' && pos + 1 < text.Length && text[pos + 1] == '>')
                {
                    tokens.Add(new StormToken(StormTokenKind.FatArrow, "=>", pos, line, column));
                    pos += 2;
                    continue;
                }

                // --- Single-character operators ---
                StormTokenKind? opKind = ClassifyOperator(c);
                if(opKind != null)
                {
                    tokens.Add(new StormToken(opKind.Value, c.ToString(), pos, line, column));
                    pos++;
                    continue;
                }

                // --- Unrecognized character ---
                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_SYNTAX_ERROR,
                    StormDiagnosticSeverity.Error,
                    $"Unrecognized character '{c}'",
                    source.FilePath,
                    line,
                    column));

                tokens.Add(new StormToken(StormTokenKind.Error, c.ToString(), pos, line, column));
                pos++;
            }

            // Always append EndOfFile.
            var (eofLine, eofColumn) = source.GetLineAndColumn(pos);
            tokens.Add(new StormToken(StormTokenKind.EndOfFile, "", pos, eofLine, eofColumn));

            return tokens;
        }

        private static bool IsIdentStart(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        }

        private static bool IsIdentPart(char c)
        {
            return IsIdentStart(c) || (c >= '0' && c <= '9');
        }

        private static StormTokenKind ClassifyKeyword(string lexeme)
        {
            switch(lexeme)
            {
                case "rule": return StormTokenKind.RuleKeyword;
                case "token": return StormTokenKind.TokenKeyword;
                case "fragment": return StormTokenKind.FragmentKeyword;
                case "hidden": return StormTokenKind.HiddenKeyword;
                case "import": return StormTokenKind.ImportKeyword;
                case "grammar": return StormTokenKind.GrammarKeyword;
                case "as": return StormTokenKind.AsKeyword;
                case "mode": return StormTokenKind.ModeKeyword;
                case "default": return StormTokenKind.DefaultKeyword;
                case "push": return StormTokenKind.PushKeyword;
                case "pop": return StormTokenKind.PopKeyword;
                default: return StormTokenKind.Identifier;
            }
        }

        private static StormTokenKind? ClassifyOperator(char c)
        {
            switch(c)
            {
                case '=': return StormTokenKind.Equals;
                case '|': return StormTokenKind.Pipe;
                case ';': return StormTokenKind.Semicolon;
                case '(': return StormTokenKind.LeftParen;
                case ')': return StormTokenKind.RightParen;
                case '?': return StormTokenKind.Question;
                case '*': return StormTokenKind.Star;
                case '+': return StormTokenKind.Plus;
                case ':': return StormTokenKind.Colon;
                case '.': return StormTokenKind.Dot;
                case '{': return StormTokenKind.LeftBrace;
                case '}': return StormTokenKind.RightBrace;
                default: return null;
            }
        }
    }
}