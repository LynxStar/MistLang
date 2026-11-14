// Feature: first-class-functions — Unit tests for pretty printer handling of `func<...>` type references
// **Validates: Requirements 13.1, 13.7**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class FuncTypeRefPrettyPrinterTests
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
    public void FuncType_AsParameterType_PrettyPrintsLowercaseKeyword()
    {
        const string source = """
            class Test {
              void Run(func<int, string> handler) {
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("func<int, string>", printed);
    }

    [Fact]
    public void FuncType_ZeroParameter_PrettyPrintsLowercaseKeyword()
    {
        const string source = """
            class Test {
              void Run(func<void> callback) {
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("func<void>", printed);
    }

    [Fact]
    public void FuncType_VoidReturn_PrettyPrintsLowercaseKeyword()
    {
        const string source = """
            class Test {
              void Run(func<int, void> handler) {
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("func<int, void>", printed);
    }

    [Fact]
    public void FuncType_Nested_PrettyPrintsLowercaseKeyword()
    {
        const string source = """
            class Test {
              void Run(func<int, func<int, int>> curried) {
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("func<int, func<int, int>>", printed);
    }

    [Fact]
    public void FuncType_AsUnionMember_PrettyPrintsLowercaseKeyword()
    {
        const string source = """
            class Test {
              void Run(func<int, bool> | string value) {
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("func<int, bool> | string", printed);
    }

    [Fact]
    public void FuncType_RoundTripIsStable()
    {
        const string source = """
            class Test {
              void Run(func<int, string> handler) {
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