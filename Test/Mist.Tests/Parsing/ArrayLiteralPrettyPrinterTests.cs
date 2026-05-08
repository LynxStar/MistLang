// Feature: array-literals — Unit tests for pretty printer handling of array literal expressions
// **Validates: Requirements 9.1, 9.2, 9.3**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests verifying that the pretty printer correctly reproduces array literal syntax.
/// Uses in-process MistParser and MistPrettyPrinter — no process spawning.
/// </summary>
public class ArrayLiteralPrettyPrinterTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    /// <summary>
    /// Parses the source, pretty-prints it, and returns the pretty-printed output.
    /// </summary>
    private static string PrettyPrint(string source)
    {
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source);
    }

    // ─── Requirement 9.1: [1, 2, 3] pretty-prints as [1, 2, 3] ──────────

    [Fact]
    public void ArrayLiteral_PrettyPrintsWithCommaSpace()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [1, 2, 3];
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("[1, 2, 3]", printed);
    }

    // ─── Requirement 9.1: Nested [[1, 2], [3, 4]] pretty-prints correctly ─

    [Fact]
    public void NestedArrayLiteral_PrettyPrintsCorrectly()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [[1, 2], [3, 4]];
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("[[1, 2], [3, 4]]", printed);
    }

    // ─── Requirement 9.2: Empty array [] pretty-prints as [] ──────────────

    [Fact]
    public void EmptyArrayLiteral_PrettyPrintsAsBrackets()
    {
        const string source = """
            class Test {
              void Run() {
                int[] x = [];
              }
            }
            """;

        var printed = PrettyPrint(source);

        // The empty array literal should pretty-print as []
        Assert.Contains("[]", printed);
    }

    // ─── Requirement 9.2: int[] type literal pretty-prints as int[] ──────

    [Fact]
    public void TypeLiteralIntArray_PrettyPrintsAsIntBrackets()
    {
        const string source = """
            class Test {
              void Run() {
                int[] x = int[];
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("int[]", printed);
    }

    // ─── Requirement 9.3: Round-trip stability ───────────────────────────

    [Fact]
    public void ArrayLiteral_RoundTripIsStable()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [1, 2, 3];
              }
            }
            """;

        // Parse → print → parse → print should produce identical output
        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    [Fact]
    public void NestedArrayLiteral_RoundTripIsStable()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [[1, 2], [3, 4]];
              }
            }
            """;

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }
}
