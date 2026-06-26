// Feature: first-class-functions — Unit tests for pretty printer handling of `func<...>` type references
// **Validates: Requirements 13.1, 13.7**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests verifying that the pretty printer reproduces `func<...>` type references using the
/// lowercase keyword form. Uses in-process MistParser and MistPrettyPrinter — no process spawning.
/// </summary>
public class FuncTypeRefPrettyPrinterTests
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

    private static string PrettyPrint(string source)
    {
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source);
    }

    // ─── Requirement 13.1: func<int, string> emits the lowercase keyword form ─

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

    // ─── Requirement 13.1: func<void> (zero-parameter) emits correctly ────────

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

    // ─── Requirement 13.1: func<int, void> emits correctly ───────────────────

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

    // ─── Requirement 13.1: nested func type emits correctly ──────────────────

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

    // ─── Requirement 13.1: func type as a union member emits correctly ───────

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

    // ─── Requirement 13.7: round-trip stability for func type references ─────

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