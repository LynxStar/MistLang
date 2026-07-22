// Feature: storm-anonymous-groups -- round-trip pretty-printer coverage for
// same-precedence multi-operand binary chains (the Group0 lists on AdditiveNode,
// MultiplicativeNode, EqualityNode, and RelationalComparisonRelationalNode). A
// pretty printer that read the old last-wins scalar shim instead of iterating
// Group0 would drop the middle operand(s) -- e.g. print "10 - 5" for "10 + 3 - 5".
// These tests fail in that case. Uses in-process MistParser and MistPrettyPrinter;
// no process spawning.

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Verifies the pretty printer reproduces EVERY operator and operand of a
/// same-precedence multi-operand chain, rather than collapsing to the last-wins
/// scalar the pre-migration shim exposed. Covers the four struct-projected binary
/// nodes: Additive, Multiplicative, Equality, and RelationalComparison.
/// </summary>
public class BinaryChainPrettyPrinterRoundTripTests
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

    private static void AssertRoundTripStable(string source)
    {
        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    [Fact]
    public void AdditiveChain_MixedOperators_PreservesAllOperands()
    {
        const string source = """
            class Test {
              void Run() {
                var r = 10 + 3 - 5;
              }
            }
            """;

        var printed = PrettyPrint(source);

        // Falsified if the middle operand is dropped (last-wins would print "10 - 5").
        Assert.Contains("10 + 3 - 5", printed);
    }

    [Fact]
    public void AdditiveChain_FourOperands_PreservesAllOperands()
    {
        const string source = """
            class Test {
              void Run() {
                var z = 1 + 2 + 3 + 4;
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("1 + 2 + 3 + 4", printed);
    }

    [Fact]
    public void MultiplicativeChain_ThreeOperands_PreservesAllOperands()
    {
        const string source = """
            class Test {
              void Run() {
                var m = 2 * 4 * 3;
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("2 * 4 * 3", printed);
    }

    [Fact]
    public void EqualityChain_ThreeOperands_PreservesAllOperands()
    {
        const string source = """
            class Test {
              void Run() {
                var e = 1 == 2 == 3;
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("1 == 2 == 3", printed);
    }

    [Fact]
    public void RelationalChain_ThreeOperands_PreservesAllOperands()
    {
        const string source = """
            class Test {
              void Run() {
                var c = 1 < 2 < 3;
              }
            }
            """;

        var printed = PrettyPrint(source);

        Assert.Contains("1 < 2 < 3", printed);
    }

    [Fact]
    public void BinaryChains_RoundTripIsStable()
    {
        AssertRoundTripStable("""
            class Test {
              void Run() {
                var r = 10 + 3 - 5;
                var z = 1 + 2 + 3 + 4;
                var m = 2 * 4 * 3;
                var e = 1 == 2 == 3;
                var c = 1 < 2 < 3;
              }
            }
            """);
    }
}