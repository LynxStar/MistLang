using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class VarForeachParseTest
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static List<Diagnostic> ParseAndGetErrors(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        _ = Parser.Parse(tokens, bag);

        return bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    [Fact]
    public void Foreach_WithVar_ParsesCorrectly()
    {
        const string source = """
            class Program {
                public static void Run() {
                    var items = [1, 2, 3];
                    foreach (var x in items) {
                        Console.WriteLine(x);
                    }
                }
            }
            """;

        var errors = ParseAndGetErrors(source);
        Assert.True(errors.Count == 0,
            $"Parse errors:\n  {string.Join("\n  ", errors.Select(e => $"{e.Id}: {e.Message}"))}");
    }

    [Fact]
    public void Foreach_WithExplicitType_StillWorksAfterGrammarChange()
    {
        const string source = """
            class Program {
                public static void Run() {
                    var items = [1, 2, 3];
                    foreach (int x in items) {
                        Console.WriteLine(x);
                    }
                }
            }
            """;

        var errors = ParseAndGetErrors(source);
        Assert.True(errors.Count == 0,
            $"Parse errors:\n  {string.Join("\n  ", errors.Select(e => $"{e.Id}: {e.Message}"))}");
    }

    [Fact]
    public void Foreach_WithVar_AndMemberAccess_ParsesCorrectly()
    {
        const string source = """
            class Program {
                public static void Run() {
                    var states = [
                        WorkItemState.Pending,
                        WorkItemState.Searching
                    ];
                    foreach (var target in states) {
                        Assert.False(StateMachine.TryTransition(WorkItemState.Complete, target));
                    }
                }
            }
            """;

        var errors = ParseAndGetErrors(source);
        Assert.True(errors.Count == 0,
            $"Parse errors:\n  {string.Join("\n  ", errors.Select(e => $"{e.Id}: {e.Message}"))}");
    }
}