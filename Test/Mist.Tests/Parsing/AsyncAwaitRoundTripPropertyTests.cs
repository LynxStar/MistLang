// Feature: structured-concurrency, Property 1: Async/Await Syntax Round-Trip
// **Validates: Requirements 12.3, 12.4, 12.5, 12.6, 12.7**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// Printed-text equality stands in for structural AST equality because the printer is deterministic and one-to-one on structure.
public class AsyncAwaitRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    public static Arbitrary<AsyncAwaitSourceScenario> ArbitraryAsyncAwaitSourceScenario()
    {
        return MistAsyncAwaitGenerator.GenAsyncAwaitSource().ToArbitrary();
    }

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncAwaitRoundTripPropertyTests) })]
    public Property RoundTrip_AsyncAwait_ParsePrintParseIsIdempotent(AsyncAwaitSourceScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var ast2 = ParseOrFail(printed1);

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nScenario: {scenario.Description}\nSource:\n{scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncAwaitRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsAsyncKeyword(AsyncAwaitSourceScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        // The output should contain the async keyword (all scenarios include async)
        var containsAsync = printed1.Contains("async");

        return containsAsync.ToProperty()
            .Label($"No 'async' keyword found in output:\n{printed1}\nScenario: {scenario.Description}");
    }
}