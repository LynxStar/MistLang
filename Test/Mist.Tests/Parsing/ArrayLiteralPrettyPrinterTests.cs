// Feature: array-literals
// **Validates: Requirements 9.1, 9.2, 9.3**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class ArrayLiteralPrettyPrinterTests
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

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static string PrettyPrint(string source)
    {
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source);
    }

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

        Assert.Contains("[]", printed);
    }

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