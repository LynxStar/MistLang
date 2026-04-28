using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;

namespace Storm.SourceGenerator.Tests
{
    public class StormLexerTests
    {
        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------

        private static List<StormToken> Tokenize(string input, out List<StormDiagnostic> diagnostics)
        {
            diagnostics = new List<StormDiagnostic>();
            var source = new StormSourceText("test.storm", input);
            return StormLexer.Tokenize(source, diagnostics);
        }

        private static List<StormToken> Tokenize(string input)
        {
            return Tokenize(input, out _);
        }

        /// <summary>Returns tokens excluding the trailing EndOfFile.</summary>
        private static List<StormToken> ContentTokens(string input)
        {
            return Tokenize(input).Where(t => t.Kind != StormTokenKind.EndOfFile).ToList();
        }

        // ---------------------------------------------------------------
        //  Keywords
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("rule", StormTokenKind.RuleKeyword)]
        [InlineData("token", StormTokenKind.TokenKeyword)]
        [InlineData("fragment", StormTokenKind.FragmentKeyword)]
        [InlineData("hidden", StormTokenKind.HiddenKeyword)]
        [InlineData("import", StormTokenKind.ImportKeyword)]
        [InlineData("grammar", StormTokenKind.GrammarKeyword)]
        [InlineData("as", StormTokenKind.AsKeyword)]
        public void Tokenize_Keywords(string keyword, StormTokenKind expectedKind)
        {
            var tokens = ContentTokens(keyword);
            Assert.Single(tokens);
            Assert.Equal(expectedKind, tokens[0].Kind);
            Assert.Equal(keyword, tokens[0].Lexeme);
        }

        // ---------------------------------------------------------------
        //  Identifiers
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("Foo")]
        [InlineData("_bar")]
        [InlineData("baz123")]
        [InlineData("A")]
        public void Tokenize_Identifiers(string ident)
        {
            var tokens = ContentTokens(ident);
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.Identifier, tokens[0].Kind);
            Assert.Equal(ident, tokens[0].Lexeme);
        }

        [Fact]
        public void Tokenize_IdentifierNotKeyword()
        {
            // "rules" is not the keyword "rule"
            var tokens = ContentTokens("rules");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.Identifier, tokens[0].Kind);
            Assert.Equal("rules", tokens[0].Lexeme);
        }

        // ---------------------------------------------------------------
        //  String literals
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_StringLiteral_Simple()
        {
            var tokens = ContentTokens("\"hello\"");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.StringLiteral, tokens[0].Kind);
            Assert.Equal("\"hello\"", tokens[0].Lexeme);
        }

        [Fact]
        public void Tokenize_StringLiteral_WithEscape()
        {
            var tokens = ContentTokens("\"he\\\"llo\"");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.StringLiteral, tokens[0].Kind);
            Assert.Equal("\"he\\\"llo\"", tokens[0].Lexeme);
        }

        [Fact]
        public void Tokenize_StringLiteral_Unterminated()
        {
            var tokens = Tokenize("\"hello", out var diags);
            Assert.Contains(tokens, t => t.Kind == StormTokenKind.StringLiteral);
            Assert.Single(diags);
            Assert.Contains("Unterminated string literal", diags[0].Message);
        }

        // ---------------------------------------------------------------
        //  Character classes
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_CharClass_Simple()
        {
            var tokens = ContentTokens("[a-z]");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.CharClass, tokens[0].Kind);
            Assert.Equal("[a-z]", tokens[0].Lexeme);
        }

        [Fact]
        public void Tokenize_CharClass_Negated()
        {
            var tokens = ContentTokens("[^\"\\\\]");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.CharClass, tokens[0].Kind);
        }

        [Fact]
        public void Tokenize_CharClass_Unterminated()
        {
            var tokens = Tokenize("[abc", out var diags);
            Assert.Contains(tokens, t => t.Kind == StormTokenKind.CharClass);
            Assert.Single(diags);
            Assert.Contains("Unterminated character class", diags[0].Message);
        }

        // ---------------------------------------------------------------
        //  Operators / punctuation
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("=", StormTokenKind.Equals)]
        [InlineData("|", StormTokenKind.Pipe)]
        [InlineData(";", StormTokenKind.Semicolon)]
        [InlineData("(", StormTokenKind.LeftParen)]
        [InlineData(")", StormTokenKind.RightParen)]
        [InlineData("?", StormTokenKind.Question)]
        [InlineData("*", StormTokenKind.Star)]
        [InlineData("+", StormTokenKind.Plus)]
        [InlineData(":", StormTokenKind.Colon)]
        [InlineData(".", StormTokenKind.Dot)]
        public void Tokenize_Operators(string op, StormTokenKind expectedKind)
        {
            var tokens = ContentTokens(op);
            Assert.Single(tokens);
            Assert.Equal(expectedKind, tokens[0].Kind);
            Assert.Equal(op, tokens[0].Lexeme);
        }

        // ---------------------------------------------------------------
        //  Comments
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_LineComment_Skipped()
        {
            var tokens = ContentTokens("// this is a comment");
            Assert.Empty(tokens);
        }

        [Fact]
        public void Tokenize_LineComment_BeforeToken()
        {
            var tokens = ContentTokens("// comment\nrule");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.RuleKeyword, tokens[0].Kind);
        }

        [Fact]
        public void Tokenize_LineComment_AfterToken()
        {
            var tokens = ContentTokens("rule // comment");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.RuleKeyword, tokens[0].Kind);
        }

        // ---------------------------------------------------------------
        //  Whitespace
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_WhitespaceSkipped()
        {
            var tokens = ContentTokens("  rule  Foo  ");
            Assert.Equal(2, tokens.Count);
            Assert.Equal(StormTokenKind.RuleKeyword, tokens[0].Kind);
            Assert.Equal(StormTokenKind.Identifier, tokens[1].Kind);
        }

        // ---------------------------------------------------------------
        //  EndOfFile
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_EmptyInput_ProducesEOF()
        {
            var tokens = Tokenize("");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.EndOfFile, tokens[0].Kind);
        }

        [Fact]
        public void Tokenize_AlwaysEndsWithEOF()
        {
            var tokens = Tokenize("rule Foo = Bar;");
            Assert.Equal(StormTokenKind.EndOfFile, tokens.Last().Kind);
        }

        // ---------------------------------------------------------------
        //  Error tokens
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_UnrecognizedCharacter_ProducesErrorToken()
        {
            var tokens = Tokenize("@", out var diags);
            Assert.Contains(tokens, t => t.Kind == StormTokenKind.Error);
            Assert.Single(diags);
            Assert.Contains("Unrecognized character", diags[0].Message);
        }

        // ---------------------------------------------------------------
        //  Line / column tracking
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_LineAndColumn_Correct()
        {
            var tokens = ContentTokens("rule\nFoo");
            Assert.Equal(2, tokens.Count);
            // "rule" at line 1, col 1
            Assert.Equal(1, tokens[0].Line);
            Assert.Equal(1, tokens[0].Column);
            // "Foo" at line 2, col 1
            Assert.Equal(2, tokens[1].Line);
            Assert.Equal(1, tokens[1].Column);
        }

        // ---------------------------------------------------------------
        //  Regex literals
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_RegexLiteral_Simple()
        {
            var tokens = ContentTokens("`abc`");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.RegexLiteral, tokens[0].Kind);
            Assert.Equal("`abc`", tokens[0].Lexeme);
        }

        [Fact]
        public void Tokenize_RegexLiteral_EscapedBacktick()
        {
            var tokens = ContentTokens(@"`a\`b`");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.RegexLiteral, tokens[0].Kind);
            Assert.Equal(@"`a\`b`", tokens[0].Lexeme);
        }

        [Fact]
        public void Tokenize_RegexLiteral_Unterminated()
        {
            var tokens = Tokenize("`abc", out var diags);
            Assert.Contains(tokens, t => t.Kind == StormTokenKind.RegexLiteral);
            Assert.Single(diags);
            Assert.Contains("Unterminated regex literal", diags[0].Message);
        }

        [Fact]
        public void Tokenize_RegexLiteral_Empty()
        {
            var tokens = ContentTokens("``");
            Assert.Single(tokens);
            Assert.Equal(StormTokenKind.RegexLiteral, tokens[0].Kind);
            Assert.Equal("``", tokens[0].Lexeme);
        }

        // ---------------------------------------------------------------
        //  Full declaration tokenization
        // ---------------------------------------------------------------

        [Fact]
        public void Tokenize_FullDeclaration()
        {
            var tokens = ContentTokens("token Ident = [a-zA-Z_] [a-zA-Z0-9_]*;");
            var kinds = tokens.Select(t => t.Kind).ToList();
            Assert.Equal(new[]
            {
                StormTokenKind.TokenKeyword,
                StormTokenKind.Identifier,
                StormTokenKind.Equals,
                StormTokenKind.CharClass,
                StormTokenKind.CharClass,
                StormTokenKind.Star,
                StormTokenKind.Semicolon,
            }, kinds);
        }
    }
}
