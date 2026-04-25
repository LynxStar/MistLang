using System;
using System.Collections.Generic;
using Storm.SourceGenerator.Diagnostics;

namespace Storm.SourceGenerator.Frontend
{
    /// <summary>
    /// Hand-written recursive-descent parser for Storm grammar files.
    /// Consumes a token list from <see cref="StormLexer"/> and produces
    /// a <see cref="StormFile"/> AST node.
    /// </summary>
    public static class StormParser
    {
        /// <summary>
        /// Parses a list of Storm tokens into a <see cref="StormFile"/> AST.
        /// </summary>
        /// <param name="tokens">The token list produced by <see cref="StormLexer.Tokenize"/>.</param>
        /// <param name="source">The source text (used for diagnostic locations).</param>
        /// <param name="diagnostics">Receives any parser diagnostics.</param>
        /// <returns>A <see cref="StormFile"/> representing the parsed grammar file.</returns>
        public static StormFile Parse(List<StormToken> tokens, StormSourceText source, List<StormDiagnostic> diagnostics)
        {
            if(tokens == null)
            {
                throw new ArgumentNullException(nameof(tokens));
            }
            if(source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if(diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            var state = new ParserState(tokens, source, diagnostics);
            return state.ParseFile();
        }

        /// <summary>
        /// Internal mutable parser state. Keeps the public API clean and static.
        /// </summary>
        private sealed class ParserState
        {
            private List<StormToken> _tokens;
            private StormSourceText _source;
            private List<StormDiagnostic> _diagnostics;
            private int _pos;

            public ParserState(List<StormToken> tokens, StormSourceText source, List<StormDiagnostic> diagnostics)
            {
                _tokens = tokens;
                _source = source;
                _diagnostics = diagnostics;
                _pos = 0;
            }

            // ----------------------------------------------------------
            //  Token access helpers
            // ----------------------------------------------------------

            private StormToken Current => _pos < _tokens.Count
                ? _tokens[_pos]
                : _tokens[_tokens.Count - 1]; // EOF sentinel

            private StormToken Peek(int offset = 0)
            {
                var idx = _pos + offset;
                return idx < _tokens.Count ? _tokens[idx] : _tokens[_tokens.Count - 1];
            }

            private StormToken Advance()
            {
                var tok = Current;
                if(_pos < _tokens.Count - 1) // don't advance past EOF
                    _pos++;
                return tok;
            }

            private bool Check(StormTokenKind kind) => Current.Kind == kind;

            private bool Match(StormTokenKind kind)
            {
                if(Current.Kind == kind)
                {
                    Advance();
                    return true;
                }
                return false;
            }

            private StormToken Expect(StormTokenKind kind, string expected)
            {
                if(Current.Kind == kind)
                {
                    return Advance();
                }

                Error($"Expected {expected}, got '{Current.Lexeme}'");
                // Return a synthetic token so the caller can continue.
                return new StormToken(kind, "", Current.Offset, Current.Line, Current.Column);
            }

            private void Error(string message)
            {
                var tok = Current;
                _diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_SYNTAX_ERROR,
                    StormDiagnosticSeverity.Error,
                    message,
                    _source.FilePath,
                    tok.Line,
                    tok.Column));
            }

            // ----------------------------------------------------------
            //  Synchronization / recovery
            // ----------------------------------------------------------

            /// <summary>
            /// Skips tokens until we find one that could start a new
            /// top-level declaration or reach EOF.
            /// </summary>
            private void SynchronizeToDeclaration()
            {
                while(Current.Kind != StormTokenKind.EndOfFile)
                {
                    switch(Current.Kind)
                    {
                        case StormTokenKind.RuleKeyword:
                        case StormTokenKind.TokenKeyword:
                        case StormTokenKind.FragmentKeyword:
                        case StormTokenKind.HiddenKeyword:
                        case StormTokenKind.ImportKeyword:
                            return;
                        default:
                            Advance();
                            break;
                    }
                }
            }

            // ----------------------------------------------------------
            //  Top-level: File
            // ----------------------------------------------------------

            public StormFile ParseFile()
            {
                var imports = new List<StormImport>();
                var declarations = new List<StormDeclaration>();

                // Parse imports (must come before declarations).
                while(Current.Kind == StormTokenKind.ImportKeyword)
                {
                    var imp = ParseImport();
                    if(imp != null)
                    {
                        imports.Add(imp);
                    }
                }

                // Parse declarations until EOF.
                while(Current.Kind != StormTokenKind.EndOfFile)
                {
                    var decl = ParseDeclaration();
                    if(decl != null)
                    {
                        declarations.Add(decl);
                    }
                }

                return new StormFile(imports, declarations);
            }

            // ----------------------------------------------------------
            //  Import declarations
            // ----------------------------------------------------------

            private StormImport? ParseImport()
            {
                // import "path" ;
                // import "path" as Namespace ;
                var importTok = Advance(); // consume 'import'
                var offset = importTok.Offset;
                var line = importTok.Line;
                var column = importTok.Column;

                if(Current.Kind != StormTokenKind.StringLiteral)
                {
                    Error("Expected string literal after 'import'");
                    SynchronizeToDeclaration();
                    return null;
                }

                var pathTok = Advance();
                var path = UnquoteString(pathTok.Lexeme);

                string? alias = null;
                if(Match(StormTokenKind.AsKeyword))
                {
                    if(Current.Kind != StormTokenKind.Identifier)
                    {
                        Error("Expected namespace identifier after 'as'");
                        SynchronizeToDeclaration();
                        return new StormImport(path, null, offset, line, column);
                    }
                    alias = Advance().Lexeme;
                }

                Expect(StormTokenKind.Semicolon, "';'");
                return new StormImport(path, alias, offset, line, column);
            }

            // ----------------------------------------------------------
            //  Declarations
            // ----------------------------------------------------------

            private StormDeclaration? ParseDeclaration()
            {
                switch(Current.Kind)
                {
                    case StormTokenKind.RuleKeyword:
                        return ParseRuleDecl();
                    case StormTokenKind.TokenKeyword:
                        return ParseTokenDecl();
                    case StormTokenKind.FragmentKeyword:
                        return ParseFragmentDecl();
                    case StormTokenKind.HiddenKeyword:
                        return ParseHiddenTokenDecl();
                    default:
                        Error($"Expected declaration (rule, token, fragment, or hidden), got '{Current.Lexeme}'");
                        SynchronizeToDeclaration();
                        return null;
                }
            }

            /// <summary>rule Name = expression ;</summary>
            private StormRuleDecl ParseRuleDecl()
            {
                var kw = Advance(); // consume 'rule'
                var nameTok = Expect(StormTokenKind.Identifier, "rule name");
                Expect(StormTokenKind.Equals, "'='");
                var body = ParseExpression();
                Expect(StormTokenKind.Semicolon, "';'");
                return new StormRuleDecl(nameTok.Lexeme, body, kw.Offset, kw.Line, kw.Column);
            }

            /// <summary>token Name = expression ;</summary>
            private StormTokenDecl ParseTokenDecl()
            {
                var kw = Advance(); // consume 'token'
                var nameTok = Expect(StormTokenKind.Identifier, "token name");
                Expect(StormTokenKind.Equals, "'='");
                var body = ParseExpression();
                Expect(StormTokenKind.Semicolon, "';'");
                return new StormTokenDecl(nameTok.Lexeme, body, kw.Offset, kw.Line, kw.Column);
            }

            /// <summary>fragment Name = expression ;</summary>
            private StormFragmentDecl ParseFragmentDecl()
            {
                var kw = Advance(); // consume 'fragment'
                var nameTok = Expect(StormTokenKind.Identifier, "fragment name");
                Expect(StormTokenKind.Equals, "'='");
                var body = ParseExpression();
                Expect(StormTokenKind.Semicolon, "';'");
                return new StormFragmentDecl(nameTok.Lexeme, body, kw.Offset, kw.Line, kw.Column);
            }

            /// <summary>hidden token Name = expression ;</summary>
            private StormHiddenTokenDecl ParseHiddenTokenDecl()
            {
                var kw = Advance(); // consume 'hidden'
                Expect(StormTokenKind.TokenKeyword, "'token'");
                var nameTok = Expect(StormTokenKind.Identifier, "token name");
                Expect(StormTokenKind.Equals, "'='");
                var body = ParseExpression();
                Expect(StormTokenKind.Semicolon, "';'");
                return new StormHiddenTokenDecl(nameTok.Lexeme, body, kw.Offset, kw.Line, kw.Column);
            }

            // ----------------------------------------------------------
            //  Expression parsing — recursive descent
            //
            //  Precedence (lowest to highest):
            //    1. Alternation  ( | )
            //    2. Named alternative  ( as Name )
            //    3. Sequence  (juxtaposition)
            //    4. Field label  ( name:element )
            //    5. Postfix  ( ? * + )
            //    6. Primary  (string, charclass, ref, qualified ref, group)
            // ----------------------------------------------------------

            /// <summary>
            /// Entry point for expression parsing. Parses alternation.
            /// </summary>
            private StormExpression ParseExpression()
            {
                return ParseAlternation();
            }

            /// <summary>
            /// Alternation: namedAlt ( '|' namedAlt )*
            /// </summary>
            private StormExpression ParseAlternation()
            {
                var first = ParseNamedAlternative();
                if(!Check(StormTokenKind.Pipe))
                {
                    return first;
                }

                var alternatives = new List<StormExpression> { first };
                while(Match(StormTokenKind.Pipe))
                {
                    alternatives.Add(ParseNamedAlternative());
                }
                return new StormAlternationExpr(alternatives);
            }

            /// <summary>
            /// Named alternative: sequence ( 'as' Name )?
            /// </summary>
            private StormExpression ParseNamedAlternative()
            {
                var expr = ParseSequence();

                if(Check(StormTokenKind.AsKeyword))
                {
                    Advance(); // consume 'as'
                    var nameTok = Expect(StormTokenKind.Identifier, "alternative name");
                    return new StormNamedAlternative(expr, nameTok.Lexeme);
                }

                return expr;
            }

            /// <summary>
            /// Sequence: labeled+  (two or more → StormSequenceExpr, one → unwrap)
            /// </summary>
            private StormExpression ParseSequence()
            {
                var elements = new List<StormExpression>();
                while(IsExpressionStart())
                {
                    elements.Add(ParseLabeled());
                }

                if(elements.Count == 0)
                {
                    Error("Expected expression");
                    // Return a placeholder so the caller can continue.
                    return new StormReferenceExpr("<error>");
                }

                if(elements.Count == 1)
                {
                    return elements[0];
                }

                return new StormSequenceExpr(elements);
            }

            /// <summary>
            /// Field label: Identifier ':' postfix  |  postfix
            /// </summary>
            private StormExpression ParseLabeled()
            {
                // Look ahead: if we see Identifier followed by ':', it's a label.
                if(Current.Kind == StormTokenKind.Identifier && Peek(1).Kind == StormTokenKind.Colon)
                {
                    var labelTok = Advance(); // consume identifier
                    Advance(); // consume ':'
                    var element = ParsePostfix();
                    return new StormLabeledExpr(labelTok.Lexeme, element);
                }

                return ParsePostfix();
            }

            /// <summary>
            /// Postfix: primary ( '?' | '*' | '+' )*
            /// </summary>
            private StormExpression ParsePostfix()
            {
                var expr = ParsePrimary();

                while(true)
                {
                    if(Match(StormTokenKind.Question))
                    {
                        expr = new StormOptionalExpr(expr);
                    }
                    else if(Match(StormTokenKind.Star))
                    {
                        expr = new StormZeroOrMoreExpr(expr);
                    }
                    else if(Match(StormTokenKind.Plus))
                    {
                        expr = new StormOneOrMoreExpr(expr);
                    }
                    else
                    {
                        break;
                    }
                }

                return expr;
            }

            /// <summary>
            /// Primary: string literal | char class | reference | qualified reference | group
            /// </summary>
            private StormExpression ParsePrimary()
            {
                switch(Current.Kind)
                {
                    case StormTokenKind.StringLiteral:
                    {
                        var tok = Advance();
                        return new StormStringLiteralExpr(UnquoteString(tok.Lexeme));
                    }

                    case StormTokenKind.CharClass:
                    {
                        var tok = Advance();
                        return new StormCharClassExpr(tok.Lexeme);
                    }

                    case StormTokenKind.Identifier:
                    {
                        var nameTok = Advance();
                        // Check for qualified reference: Name.Name
                        if(Check(StormTokenKind.Dot))
                        {
                            Advance(); // consume '.'
                            var memberTok = Expect(StormTokenKind.Identifier, "qualified name");
                            return new StormQualifiedReferenceExpr(nameTok.Lexeme, memberTok.Lexeme);
                        }
                        return new StormReferenceExpr(nameTok.Lexeme);
                    }

                    case StormTokenKind.LeftParen:
                    {
                        Advance(); // consume '('
                        var inner = ParseExpression();
                        Expect(StormTokenKind.RightParen, "')'");
                        return new StormGroupExpr(inner);
                    }

                    default:
                    {
                        Error($"Unexpected token '{Current.Lexeme}'");
                        Advance(); // skip the bad token to avoid infinite loop
                        return new StormReferenceExpr("<error>");
                    }
                }
            }

            // ----------------------------------------------------------
            //  Helpers
            // ----------------------------------------------------------

            /// <summary>
            /// Returns true if the current token can start an expression element.
            /// Used by sequence parsing to know when to stop.
            /// </summary>
            private bool IsExpressionStart()
            {
                switch(Current.Kind)
                {
                    case StormTokenKind.StringLiteral:
                    case StormTokenKind.CharClass:
                    case StormTokenKind.Identifier:
                    case StormTokenKind.LeftParen:
                        return true;
                    default:
                        return false;
                }
            }

            /// <summary>
            /// Strips the surrounding double quotes from a string literal lexeme.
            /// Handles basic escape sequences.
            /// </summary>
            private static string UnquoteString(string lexeme)
            {
                if(lexeme.Length < 2 || lexeme[0] != '"')
                {
                    return lexeme;
                }

                // Strip quotes
                var inner = lexeme.Substring(1, lexeme.Length - 2);

                // Process escape sequences
                if(inner.IndexOf('\\') < 0)
                {
                    return inner;
                }

                var sb = new System.Text.StringBuilder(inner.Length);
                for(var i = 0; i < inner.Length; i++)
                {
                    if(inner[i] == '\\' && i + 1 < inner.Length)
                    {
                        var next = inner[i + 1];
                        switch(next)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            default:
                                sb.Append('\\');
                                sb.Append(next);
                                break;
                        }
                        i++; // skip the escaped character
                    }
                    else
                    {
                        sb.Append(inner[i]);
                    }
                }
                return sb.ToString();
            }
        }
    }
}
