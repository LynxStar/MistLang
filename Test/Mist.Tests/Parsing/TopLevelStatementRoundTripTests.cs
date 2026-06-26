using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Verifies the round-trip property for top-level statements:
/// parse → pretty-print → parse → pretty-print produces identical output.
///
/// Structural AST equivalence is asserted by comparing the two printed outputs.
/// Because the pretty printer is deterministic and one-to-one on structure,
/// equality of printed outputs is a strong proxy for structural equivalence.
///
/// All work runs in-process — no external process is spawned.
///
/// **Validates: Requirement 8.2**
/// </summary>
public class TopLevelStatementRoundTripTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        var cu = Assert.IsType<CompilationUnitNode>(result.Root);
        return cu;
    }

    /// <summary>
    /// Asserts the full round-trip: source → AST₁ → print₁ → AST₂ → print₂,
    /// verifying print₁ == print₂ (structural equivalence) and that both ASTs
    /// contain the expected number of StatementItemTopLevelItemNode entries.
    /// </summary>
    private static void AssertRoundTripPreserved(string source, int expectedStatementCount)
    {
        // Step 1: Parse the original source
        var ast1 = ParseOrFail(source);

        // Verify AST₁ contains top-level statements
        var stmtItems1 = ast1.Items
            .OfType<StatementItemTopLevelItemNode>()
            .ToList();
        Assert.Equal(expectedStatementCount, stmtItems1.Count);

        // Step 2: Pretty-print AST₁ → print₁
        var printed1 = MistPrettyPrinter.Print(ast1, source);
        Assert.False(string.IsNullOrWhiteSpace(printed1),
            $"Pretty printer produced empty output for source:\n{source}");

        // Step 3: Parse print₁ → AST₂
        var ast2 = ParseOrFail(printed1);

        // Verify AST₂ also contains the same number of top-level statements
        var stmtItems2 = ast2.Items
            .OfType<StatementItemTopLevelItemNode>()
            .ToList();
        Assert.Equal(expectedStatementCount, stmtItems2.Count);

        // Verify same total item count (statements + declarations)
        Assert.Equal(ast1.Items.Length, ast2.Items.Length);

        // Verify same item types in same order
        for(var i = 0; i < ast1.Items.Length; i++)
        {
            Assert.Equal(ast1.Items[i].GetType(), ast2.Items[i].GetType());
        }

        // Step 4: Pretty-print AST₂ → print₂
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Assert print₁ == print₂ (idempotence / structural equivalence)
        Assert.Equal(printed1, printed2);
    }

    [Fact]
    public void RoundTrip_SimpleVarDeclarations()
    {
        const string source = """
            var x = 1;
            var y = x;
            """;

        AssertRoundTripPreserved(source, expectedStatementCount: 2);
    }

    [Fact]
    public void RoundTrip_TypedVariableDeclaration()
    {
        const string source = """
            int x = 42;
            string name = "hello";
            """;

        AssertRoundTripPreserved(source, expectedStatementCount: 2);
    }

    [Fact]
    public void RoundTrip_ExpressionStatements()
    {
        const string source = """
            var x = 1;
            x = 2;
            """;

        AssertRoundTripPreserved(source, expectedStatementCount: 2);
    }

    [Fact]
    public void RoundTrip_IfStatement()
    {
        const string source = """
            var x = 1;
            if (x == 1) {
                var y = 2;
            }
            """;

        AssertRoundTripPreserved(source, expectedStatementCount: 2);
    }

    [Fact]
    public void RoundTrip_WhileStatement()
    {
        const string source = """
            var i = 0;
            while (i < 10) {
                i = i + 1;
            }
            """;

        AssertRoundTripPreserved(source, expectedStatementCount: 2);
    }

    [Fact]
    public void RoundTrip_ForStatement()
    {
        const string source = """
            for (var i = 0; i < 5; i = i + 1) {
                var x = i;
            }
            """;

        AssertRoundTripPreserved(source, expectedStatementCount: 1);
    }

    [Fact]
    public void RoundTrip_ReturnStatement()
    {
        const string source = """
            var x = 1;
            return x;
            """;

        AssertRoundTripPreserved(source, expectedStatementCount: 2);
    }

    [Fact]
    public void RoundTrip_StatementsWithTypeDeclaration()
    {
        const string source = """
            var x = 1;
            var y = 2;
            class Foo {
            }
            """;

        // 2 statements + 1 type declaration = 3 items total, but only 2 are statements
        AssertRoundTripPreserved(source, expectedStatementCount: 2);
    }

    [Fact]
    public void RoundTrip_MultipleStatementTypes()
    {
        const string source = """
            var x = 1;
            int y = 2;
            if (x == y) {
                var z = 3;
            }
            while (x < 10) {
                x = x + 1;
            }
            return 0;
            """;

        AssertRoundTripPreserved(source, expectedStatementCount: 5);
    }

    /// <summary>
    /// Verifies that top-level statements are printed at column 0 — no leading
    /// whitespace/indentation — confirming they are NOT indented as if inside a
    /// class or method body.
    ///
    /// **Validates: Requirement 8.1**
    /// </summary>
    [Fact]
    public void PrettyPrint_TopLevelStatements_HaveNoIndentation()
    {
        const string source = """
            var x = 1;
            var y = x + 2;
            return y;
            """;

        var ast = ParseOrFail(source);
        var printed = MistPrettyPrinter.Print(ast, source);

        // Every non-empty line should start at column 0 (no leading whitespace)
        var lines = printed.Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        Assert.NotEmpty(lines);
        foreach(var line in lines)
        {
            Assert.False(char.IsWhiteSpace(line[0]),
                $"Top-level statement has unexpected leading indentation: \"{line}\"");
        }
    }

    /// <summary>
    /// Verifies that nested statements within top-level control flow ARE indented
    /// (only the top-level statements themselves start at column 0).
    ///
    /// **Validates: Requirement 8.1**
    /// </summary>
    [Fact]
    public void PrettyPrint_NestedStatements_AreIndented()
    {
        const string source = """
            if (true) {
                var x = 1;
            }
            """;

        var ast = ParseOrFail(source);
        var printed = MistPrettyPrinter.Print(ast, source);

        var lines = printed.Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        // The "if" line should start at column 0
        Assert.StartsWith("if", lines[0]);

        // The nested "var x = 1;" line should be indented
        var nestedLine = lines.First(l => l.Contains("var x"));
        Assert.True(char.IsWhiteSpace(nestedLine[0]),
            $"Nested statement should be indented but was: \"{nestedLine}\"");
    }
}