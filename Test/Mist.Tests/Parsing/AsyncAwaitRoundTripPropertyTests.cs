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

/// <summary>
/// Property-based tests for async/await syntax parse–print–parse round trip.
///
/// **Validates: Requirements 12.3, 12.4, 12.5, 12.6, 12.7**
///
/// For any valid Mist source program containing <c>async</c> modifiers on methods,
/// <c>async</c> functor expressions, and <c>await</c> expressions, parsing the source text
/// and then pretty-printing the resulting AST and then parsing the pretty-printed text
/// SHALL produce an equivalent AST.
///
/// Structural equivalence is verified by comparing pretty-printed outputs: because the
/// pretty printer is deterministic and one-to-one on structure, equality of printed
/// outputs is a strong proxy for structural equivalence.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AsyncAwaitRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<AsyncAwaitSourceScenario> ArbitraryAsyncAwaitSourceScenario()
    {
        return MistAsyncAwaitGenerator.GenAsyncAwaitSource().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Parse–Print–Parse Round Trip
    //
    // **Validates: Requirements 12.3, 12.4, 12.5, 12.6, 12.7**
    //
    // For any valid Mist source with async modifiers, async functor expressions,
    // and await expressions, parsing the source to an AST, pretty-printing the AST
    // back to source text, and parsing the result again SHALL produce a structurally
    // equivalent AST (verified by comparing their pretty-printed forms).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncAwaitRoundTripPropertyTests) })]
    public Property RoundTrip_AsyncAwait_ParsePrintParseIsIdempotent(AsyncAwaitSourceScenario scenario)
    {
        // Step 1: Parse the generated source → AST1
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify structural equivalence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nScenario: {scenario.Description}\nSource:\n{scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncAwaitRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsAsyncKeyword(AsyncAwaitSourceScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        // The output should contain the async keyword (all scenarios include async)
        var containsAsync = printed1.Contains("async");

        return containsAsync.ToProperty()
            .Label($"No 'async' keyword found in output:\n{printed1}\nScenario: {scenario.Description}");
    }
}